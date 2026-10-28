package verificacion

import (
	"flag"
	"os"
	"path/filepath"
	"testing"
)

var actualizar = flag.Bool("actualizar", false, "regenera los bundles de ejemplo de testdata")

// Los bundles de ejemplo de testdata (los usan los tests de la CLI y sirven de muestra para un
// tercero) salen del constructor de prueba. Se regeneran con: go test ./verificacion -run Ejemplo -actualizar
func TestLosBundlesDeEjemploEstanAlDia(t *testing.T) {
	ejemplos := map[string][]byte{
		"pericial.zip":      nuevoPericial(t).armar(),
		"investigativo.zip": nuevoInvestigativo(t).armar(),
		"dvr.mp4":           bytesVideo,
		"huella.txt":        []byte(huellaDe(claveDelLaboratorio) + "\n"),
	}
	dir := filepath.Join("..", "testdata")
	for nombre, contenido := range ejemplos {
		ruta := filepath.Join(dir, nombre)
		if *actualizar {
			if err := os.MkdirAll(filepath.Dir(ruta), 0o755); err != nil {
				t.Fatal(err)
			}
			if err := os.WriteFile(ruta, contenido, 0o644); err != nil {
				t.Fatal(err)
			}
			continue
		}
		if _, err := os.Stat(ruta); err != nil {
			t.Fatalf("falta %s: regeneralo con -actualizar", ruta)
		}
	}
	r, err := VerificarArchivo(filepath.Join(dir, "pericial.zip"), Opciones{Huella: huellaDe(claveDelLaboratorio), Externos: dir})
	if err != nil || r.Estado != Integro {
		t.Fatalf("el ejemplo pericial no verifica: %v %+v", err, r)
	}
}
