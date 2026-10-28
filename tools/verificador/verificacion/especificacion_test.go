package verificacion

import (
	"bufio"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"testing"
)

// La lista blanca del pericial es la de docs/formato-bundle-v1.md; el servidor fija la misma
// (ContenidoPericialTests). Si cambia de un lado, este test o aquél falla.
func TestLaListaBlancaEsLaDeLaEspecificacion(t *testing.T) {
	f, err := os.Open(filepath.Join("..", "..", "..", "docs", "formato-bundle-v1.md"))
	if err != nil {
		t.Fatal(err)
	}
	defer f.Close()
	var linea string
	for s := bufio.NewScanner(f); s.Scan(); {
		if strings.Contains(s.Text(), "lista blanca cerrada:") {
			linea = s.Text()
		}
	}
	tipos := regexp.MustCompile("`([A-Za-z]+)`").FindAllStringSubmatch(linea[strings.Index(linea, ":"):], -1)
	if len(tipos) != len(tiposPericiales) {
		t.Fatalf("la especificación lista %d tipos y el verificador %d", len(tipos), len(tiposPericiales))
	}
	for _, m := range tipos {
		if !tiposPericiales[m[1]] {
			t.Fatalf("%s está en la especificación pero no en el verificador", m[1])
		}
	}
}
