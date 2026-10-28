package verificacion

import (
	"archive/zip"
	"bytes"
	"crypto/ed25519"
	"crypto/sha256"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"sort"
	"strings"
	"testing"
)

// Constructor de bundles de prueba, armado a mano según docs/formato-bundle-v1.md. Los ganchos
// permiten manipular cada etapa: antes de hashear (lo que haría un servidor comprometido que
// recalcula todo), después de hashear y firmar (lo que haría alguien que edita el bundle).

const (
	casoDePrueba     = "22222222-2222-2222-2222-222222222222"
	idOriginal       = "aaaaaaaa-0000-0000-0000-000000000001"
	idDerivado       = "aaaaaaaa-0000-0000-0000-000000000002"
	idDerivadoIa     = "aaaaaaaa-0000-0000-0000-000000000003"
	idVideo          = "aaaaaaaa-0000-0000-0000-000000000004"
	via              = "AdmisibleEnJuicio"
	viaInvestigacion = "SoloInvestigacion"
)

var (
	claveDelLaboratorio = ed25519.NewKeyFromSeed(bytes.Repeat([]byte{7}, 32))
	otraClave           = ed25519.NewKeyFromSeed(bytes.Repeat([]byte{9}, 32))

	bytesOriginal   = []byte("bytes crudos de la foto original")
	bytesDerivado   = []byte("png del recorte")
	bytesDerivadoIa = []byte("png de la super-resolución")
	bytesVideo      = []byte("export del DVR, demasiado grande para embeber")
)

func huellaDe(clave ed25519.PrivateKey) string {
	h := sha256.Sum256(clave.Public().(ed25519.PublicKey))
	return "ed25519:" + hex.EncodeToString(h[:])
}

func sha(b []byte) string {
	h := sha256.Sum256(b)
	return hex.EncodeToString(h[:])
}

type asientoFuente struct {
	tipo      string
	evidencia *string
	actor     string
	detalle   string
	opaco     bool
}

type constructor struct {
	t                 *testing.T
	tipo              string
	fuentes           []asientoFuente
	evidencias        []Evidencia
	contenidos        map[string][]byte // ruta -> bytes de las evidencias embebidas
	sellarEn          []int64
	clave             ed25519.PrivateKey
	antesDeHashear    func(c *constructor)
	despuesDeHashear  func(asientos []Asiento)
	alSellar          func(sellos []Sello) []Sello
	alManifiesto      func(m *Manifiesto)
	alZip             func(entradas map[string][]byte)
	manifiestoCrudo   func(b []byte) []byte
	sinFirmaDeEntrada bool
}

func ptr(s string) *string { return &s }

func detalleIngesta(contenido []byte, viaDeEntrada string) string {
	return fmt.Sprintf(`{"sha256":%q,"tamanoBytes":%d,"nombreOriginal":"x","via":%q,"controlCruzado":{"estado":"coincide"}}`,
		sha(contenido), len(contenido), viaDeEntrada)
}

func detalleOperacion(formato, idEntrada string, entrada []byte, idSalida string, salida []byte, viaDeSalida string) string {
	return fmt.Sprintf(`{"formato":%q,"operacion":"opencv.recorte/v1","parametros":{"x":1},"entrada":{"evidenciaId":%q,"sha256":%q},`+
		`"salida":{"evidenciaId":%q,"sha256":%q,"tamanoBytes":%d,"formato":"png"},"entorno":{"opencv":"4.14.0"},"via":%q}`,
		formato, idEntrada, sha(entrada), idSalida, sha(salida), len(salida), viaDeSalida)
}

// Un caso con: apertura, la foto original, un recorte OpenCV, un análisis de IA sobre la foto,
// una super-resolución sobre el recorte, el video grande y un acceso al recorte.
func nuevoPericial(t *testing.T) *constructor {
	c := &constructor{t: t, tipo: "pericial", clave: claveDelLaboratorio, contenidos: map[string][]byte{}}
	c.fuentes = []asientoFuente{
		{tipo: "CasoAbierto", actor: "ana", detalle: `{"numeroExpediente":"EXP-1","descripcion":"Robo","analistasAsignados":["ana"]}`},
		{tipo: "EvidenciaIngestada", evidencia: ptr(idOriginal), actor: "ana", detalle: detalleIngesta(bytesOriginal, via)},
		{tipo: "OperacionAplicada", evidencia: ptr(idDerivado), actor: "ana",
			detalle: detalleOperacion("forensic-video/operacion/v1", idOriginal, bytesOriginal, idDerivado, bytesDerivado, via)},
		{tipo: "HallazgosRegistrados", evidencia: ptr(idOriginal), actor: "ana", detalle: `{"hallazgos":[{"clase":"auto"}]}`, opaco: true},
		{tipo: "OperacionIaAplicada", evidencia: ptr(idDerivadoIa), actor: "ana", opaco: true,
			detalle: detalleOperacion("forensic-video/operacion-ia/v1", idDerivado, bytesDerivado, idDerivadoIa, bytesDerivadoIa, viaInvestigacion)},
		{tipo: "EvidenciaIngestada", evidencia: ptr(idVideo), actor: "beto", detalle: detalleIngesta(bytesVideo, via)},
		{tipo: "EvidenciaAccedida", evidencia: ptr(idDerivado), actor: "ana", detalle: `{"integridadVerificada":true}`},
	}
	c.embeber(idOriginal, bytesOriginal, 2)
	c.embeber(idDerivado, bytesDerivado, 3)
	c.evidencias = append(c.evidencias, Evidencia{
		ID: idVideo, Sha256: sha(bytesVideo), TamanoBytes: int64(len(bytesVideo)), Nombre: "dvr.mp4", AsientoDeOrigen: 6,
	})
	c.sellarEn = []int64{3, 7}
	return c
}

// El mismo caso como investigativo: todo completo y con el derivado de IA.
func nuevoInvestigativo(t *testing.T) *constructor {
	c := nuevoPericial(t)
	c.tipo = "investigativo"
	for i := range c.fuentes {
		c.fuentes[i].opaco = false
	}
	c.embeber(idDerivadoIa, bytesDerivadoIa, 5)
	return c
}

func (c *constructor) embeber(id string, contenido []byte, origen int64) {
	ruta := "evidencias/" + sha(contenido)
	c.contenidos[ruta] = contenido
	c.evidencias = append(c.evidencias, Evidencia{
		ID: id, Sha256: sha(contenido), TamanoBytes: int64(len(contenido)), Nombre: id + ".bin",
		AsientoDeOrigen: origen, Embebida: true, Ruta: ptr(ruta),
	})
}

func (c *constructor) asientos() []Asiento {
	anterior := strings.Repeat("0", 64)
	var asientos []Asiento
	for i, f := range c.fuentes {
		a := Asiento{
			Secuencia: int64(i + 1), ID: fmt.Sprintf("bbbbbbbb-0000-0000-0000-%012d", i+1), Tipo: f.tipo,
			Evidencia: f.evidencia, Actor: f.actor, Timestamp: fmt.Sprintf("2026-10-28T12:00:%02d.000000Z", i),
			Detalle: f.detalle, HashAnterior: anterior,
		}
		a.Hash = HashDeAsiento(casoDePrueba, a)
		anterior = a.Hash
		if f.opaco {
			a = Asiento{Secuencia: a.Secuencia, Opaco: true, HashAnterior: a.HashAnterior, Hash: a.Hash}
		}
		asientos = append(asientos, a)
	}
	return asientos
}

func (c *constructor) sellos(asientos []Asiento) []Sello {
	var sellos []Sello
	for _, s := range c.sellarEn {
		sello := Sello{
			Caso: casoDePrueba, Secuencia: s, HashCabeza: asientos[s-1].Hash,
			SelladoEn: fmt.Sprintf("2026-10-28T13:00:%02d.000000Z", s), ClaveID: huellaDe(claveDelLaboratorio),
		}
		sello.Firma = base64.StdEncoding.EncodeToString(ed25519.Sign(claveDelLaboratorio, MensajeDeSello(sello)))
		sellos = append(sellos, sello)
	}
	return sellos
}

func jsonDe(t *testing.T, v any) []byte {
	b, err := json.MarshalIndent(v, "", "  ")
	if err != nil {
		t.Fatal(err)
	}
	return b
}

// armar devuelve los bytes del ZIP.
func (c *constructor) armar() []byte {
	if c.antesDeHashear != nil {
		c.antesDeHashear(c)
	}
	asientos := c.asientos()
	sellos := c.sellos(asientos)
	if c.despuesDeHashear != nil {
		c.despuesDeHashear(asientos)
	}
	if c.alSellar != nil {
		sellos = c.alSellar(sellos)
	}

	entradas := map[string][]byte{
		"cadena.json": jsonDe(c.t, Cadena{Formato: "forensic-video/custodia/v1", Caso: casoDePrueba, Asientos: asientos}),
		"sellos.json": jsonDe(c.t, Sellos{Formato: "forensic-video/sello/v1", Sellos: sellos}),
		"informe.pdf": []byte("%PDF-1.7 de prueba"),
		"LEEME.txt":   []byte("Verificá este bundle con el verificador."),
	}
	m := Manifiesto{
		Formato: "forensic-video/bundle/v1", Tipo: c.tipo,
		Caso:       Caso{ID: casoDePrueba, NumeroExpediente: "EXP-1", Descripcion: "Robo"},
		GeneradoEn: "2026-10-28T14:00:00.000000Z", GeneradoPor: "ana",
		Clave:      Clave{ID: huellaDe(c.clave), Publica: hex.EncodeToString(c.clave.Public().(ed25519.PublicKey))},
		Cadena:     CadenaDelManifiesto{Hasta: int64(len(asientos)), HashCabeza: asientos[len(asientos)-1].Hash},
		Evidencias: c.evidencias,
	}
	rutas := make([]string, 0, len(entradas))
	for r := range entradas {
		rutas = append(rutas, r)
	}
	sort.Strings(rutas)
	for _, r := range rutas {
		m.Archivos = append(m.Archivos, Archivo{Ruta: r, Sha256: sha(entradas[r]), TamanoBytes: int64(len(entradas[r]))})
	}
	for r, b := range c.contenidos {
		entradas[r] = b
	}
	if c.alManifiesto != nil {
		c.alManifiesto(&m)
	}
	crudo := jsonDe(c.t, m)
	if c.manifiestoCrudo != nil {
		crudo = c.manifiestoCrudo(crudo)
	}
	entradas["manifiesto.json"] = crudo
	entradas["manifiesto.json.sig"] = []byte(base64.StdEncoding.EncodeToString(ed25519.Sign(c.clave, crudo)) + "\n")
	if c.alZip != nil {
		c.alZip(entradas)
	}
	return comprimir(c.t, entradas)
}

func comprimir(t *testing.T, entradas map[string][]byte) []byte {
	var buf bytes.Buffer
	w := zip.NewWriter(&buf)
	rutas := make([]string, 0, len(entradas))
	for r := range entradas {
		rutas = append(rutas, r)
	}
	sort.Strings(rutas)
	for _, r := range rutas {
		f, err := w.Create(r)
		if err != nil {
			t.Fatal(err)
		}
		if _, err := f.Write(entradas[r]); err != nil {
			t.Fatal(err)
		}
	}
	if err := w.Close(); err != nil {
		t.Fatal(err)
	}
	return buf.Bytes()
}
