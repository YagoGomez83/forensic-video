// Comando verificador: verifica un bundle forensic-video/bundle/v1 sin el servidor.
//
//	verificador --clave <huella> [--externos <carpeta>] [--exigir pericial|investigativo] [--json] <bundle.zip>
//
// Códigos de salida: 0 ÍNTEGRO, 1 MANIPULADO, 2 INCOMPLETO (faltan evidencias externas), 3 error de uso.
package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os"
	"strings"

	"github.com/YagoGomez83/forensic-video/tools/verificador/verificacion"
)

// version la fija el build de release (-ldflags "-X main.version=…").
var version = "desarrollo"

const (
	codigoIntegro    = 0
	codigoManipulado = 1
	codigoIncompleto = 2
	codigoDeUso      = 3
)

const ayuda = `Uso: verificador --clave <huella> [opciones] <bundle.zip>

Verifica por su cuenta un bundle forensic-video/bundle/v1: firma del manifiesto, hashes de cada
archivo, cadena de custodia encadenada, sellos del laboratorio y, en un bundle pericial, que no
contenga nada de la vía «sólo investigación». No usa el servidor ni la red.

  --clave <huella>     huella de la clave del laboratorio (ed25519:<64 hex>), obtenida por un
                       medio independiente del bundle. Obligatoria.
  --externos <dir>     carpeta con las evidencias que viajan aparte; se buscan por hash.
  --exigir <tipo>      exige un bundle "pericial" o "investigativo".
  --json               imprime el resultado completo en JSON.
  --version            imprime la versión y termina.

Códigos de salida: 0 ÍNTEGRO, 1 MANIPULADO, 2 INCOMPLETO, 3 error de uso.
`

func ejecutar(args []string, salida io.Writer) int {
	fs := flag.NewFlagSet("verificador", flag.ContinueOnError)
	fs.SetOutput(salida)
	fs.Usage = func() { fmt.Fprint(salida, ayuda) }
	clave := fs.String("clave", "", "")
	externos := fs.String("externos", "", "")
	exigir := fs.String("exigir", "", "")
	enJSON := fs.Bool("json", false, "")
	verVersion := fs.Bool("version", false, "")
	if err := fs.Parse(args); err != nil {
		return codigoDeUso
	}
	if *verVersion {
		fmt.Fprintln(salida, "verificador", version)
		return codigoIntegro
	}

	errDeUso := func(formato string, a ...any) int {
		fmt.Fprintf(salida, "Error: "+formato+"\n\n", a...)
		fmt.Fprint(salida, ayuda)
		return codigoDeUso
	}
	if fs.NArg() != 1 {
		return errDeUso("indicá un único bundle a verificar.")
	}
	huella := verificacion.NormalizarHuella(*clave)
	if !huellaValida(huella) {
		return errDeUso("--clave debe ser la huella de la clave del laboratorio: ed25519:<64 hex>.")
	}
	if *exigir != "" && *exigir != "pericial" && *exigir != "investigativo" {
		return errDeUso("--exigir admite \"pericial\" o \"investigativo\".")
	}
	if *externos != "" {
		if info, err := os.Stat(*externos); err != nil || !info.IsDir() {
			return errDeUso("la carpeta de externos %q no existe.", *externos)
		}
	}
	ruta := fs.Arg(0)
	if info, err := os.Stat(ruta); err != nil || info.IsDir() {
		return errDeUso("no se encontró el bundle %q.", ruta)
	}

	r, err := verificacion.VerificarArchivo(ruta, verificacion.Opciones{Huella: huella, Externos: *externos, ExigirTipo: *exigir})
	if err != nil {
		return errDeUso("no se pudo abrir el bundle: %v", err)
	}
	if *enJSON {
		enc := json.NewEncoder(salida)
		enc.SetIndent("", "  ")
		if err := enc.Encode(r); err != nil {
			return codigoDeUso
		}
	} else {
		imprimir(salida, ruta, r)
	}
	return codigoDe(r.Estado)
}

func huellaValida(h string) bool {
	hexa, ok := strings.CutPrefix(h, "ed25519:")
	if !ok || len(hexa) != 64 {
		return false
	}
	return strings.Trim(hexa, "0123456789abcdef") == ""
}

func codigoDe(e verificacion.Estado) int {
	switch e {
	case verificacion.Integro:
		return codigoIntegro
	case verificacion.Incompleto:
		return codigoIncompleto
	default:
		return codigoManipulado
	}
}

func imprimir(w io.Writer, ruta string, r verificacion.Resultado) {
	fmt.Fprintf(w, "verificador %s · %s\n", version, ruta)
	if r.Tipo == "investigativo" {
		fmt.Fprintln(w, "*** INVESTIGATIVO — SÓLO INVESTIGACIÓN, NO ES PRUEBA ***")
	}
	for _, c := range r.Comprobaciones {
		donde := ""
		switch {
		case c.Secuencia != nil:
			donde = fmt.Sprintf(" (asiento %d)", *c.Secuencia)
		case c.Ruta != "" && c.Nivel != verificacion.Ok:
			donde = fmt.Sprintf(" (%s)", c.Ruta)
		}
		fmt.Fprintf(w, "[%s] %s%s\n", c.Nivel, c.Mensaje, donde)
	}
	fmt.Fprintf(w, "RESULTADO: %s   (código de salida %d)\n", r.Estado, codigoDe(r.Estado))
}

func main() {
	prepararConsola()
	os.Exit(ejecutar(os.Args[1:], os.Stdout))
}
