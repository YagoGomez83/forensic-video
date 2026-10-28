package verificacion

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"strings"
	"unicode/utf8"
)

// encoding/json acepta claves repetidas (gana la última) y empareja claves sin distinguir
// mayúsculas. En un verificador eso permitiría que dos lectores vean contenidos distintos en el
// mismo archivo, así que se rechaza: claves repetidas (aun en otra caja), claves conocidas con
// otra caja, claves desconocidas y datos sobrantes.

var clavesConocidas = func() map[string]string {
	m := map[string]string{}
	for _, c := range []string{
		"formato", "tipo", "caso", "generadoEn", "generadoPor", "clave", "cadena", "archivos", "evidencias",
		"id", "numeroExpediente", "descripcion", "publica", "hasta", "hashCabeza", "ruta", "sha256",
		"tamanoBytes", "nombre", "asientoDeOrigen", "embebida", "asientos", "secuencia", "opaco",
		"evidencia", "actor", "timestamp", "detalle", "hashAnterior", "hash", "sellos", "selladoEn",
		"claveId", "firma",
	} {
		m[strings.ToLower(c)] = c
	}
	return m
}()

// decodificar lee un archivo del bundle con las reglas estrictas.
func decodificar(datos []byte, v any) error {
	if err := revisarTexto(datos, true); err != nil {
		return err
	}
	dec := json.NewDecoder(bytes.NewReader(datos))
	dec.DisallowUnknownFields()
	if err := dec.Decode(v); err != nil {
		return err
	}
	if _, err := dec.Token(); !errors.Is(err, io.EOF) {
		return errors.New("hay datos después del JSON")
	}
	return nil
}

// decodificarDetalle lee el detalle de un asiento: sin esquema fijo, pero sin claves repetidas.
func decodificarDetalle(detalle string) (map[string]any, error) {
	if err := revisarTexto([]byte(detalle), false); err != nil {
		return nil, err
	}
	dec := json.NewDecoder(strings.NewReader(detalle))
	dec.UseNumber()
	var m map[string]any
	if err := dec.Decode(&m); err != nil {
		return nil, err
	}
	if m == nil {
		return nil, errors.New("el detalle no es un objeto")
	}
	return m, nil
}

func revisarTexto(datos []byte, esquemaConocido bool) error {
	if !utf8.Valid(datos) {
		return errors.New("no es UTF-8 válido")
	}
	if bytes.HasPrefix(datos, []byte("\xef\xbb\xbf")) {
		return errors.New("empieza con un BOM")
	}
	return revisarClaves(datos, esquemaConocido)
}

// revisarClaves recorre los tokens y rechaza claves repetidas en un mismo objeto, comparadas sin
// distinguir mayúsculas. Con esquemaConocido, además rechaza una clave conocida escrita en otra caja.
func revisarClaves(datos []byte, esquemaConocido bool) error {
	dec := json.NewDecoder(bytes.NewReader(datos))
	type nivel struct {
		objeto  bool
		esClave bool
		vistas  map[string]bool
	}
	var pila []*nivel
	for {
		tok, err := dec.Token()
		if errors.Is(err, io.EOF) {
			return nil
		}
		if err != nil {
			return err
		}
		var actual *nivel
		if len(pila) > 0 {
			actual = pila[len(pila)-1]
		}
		if actual != nil && actual.objeto && actual.esClave {
			if d, ok := tok.(json.Delim); !ok || d != '}' {
				clave := tok.(string)
				minuscula := strings.ToLower(clave)
				if actual.vistas[minuscula] {
					return fmt.Errorf("la clave %q está repetida", clave)
				}
				actual.vistas[minuscula] = true
				if exacta, ok := clavesConocidas[minuscula]; esquemaConocido && ok && exacta != clave {
					return fmt.Errorf("la clave %q debería escribirse %q", clave, exacta)
				}
				actual.esClave = false
				continue
			}
		}
		switch tok {
		case json.Delim('{'):
			pila = append(pila, &nivel{objeto: true, esClave: true, vistas: map[string]bool{}})
			continue
		case json.Delim('['):
			pila = append(pila, &nivel{})
			continue
		case json.Delim('}'), json.Delim(']'):
			pila = pila[:len(pila)-1]
		}
		// Un valor completo dentro de un objeto: lo próximo es otra clave.
		if len(pila) > 0 && pila[len(pila)-1].objeto {
			pila[len(pila)-1].esClave = true
		}
	}
}
