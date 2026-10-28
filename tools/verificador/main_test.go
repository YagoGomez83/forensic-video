package main

import (
	"bytes"
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func huellaDeEjemplo(t *testing.T) string {
	b, err := os.ReadFile(filepath.Join("testdata", "huella.txt"))
	if err != nil {
		t.Fatal(err)
	}
	return strings.TrimSpace(string(b))
}

func correr(args ...string) (int, string) {
	var salida bytes.Buffer
	codigo := ejecutar(args, &salida)
	return codigo, salida.String()
}

func TestUnBundleIntegroSaleConCodigoCeroYLoDice(t *testing.T) {
	codigo, salida := correr("--clave", huellaDeEjemplo(t), "--externos", "testdata", filepath.Join("testdata", "pericial.zip"))

	if codigo != 0 || !strings.Contains(salida, "RESULTADO: ÍNTEGRO") || !strings.Contains(salida, "PERICIAL") {
		t.Fatalf("código %d:\n%s", codigo, salida)
	}
	if strings.Contains(salida, "[FALLA]") {
		t.Fatalf("no debería haber fallas:\n%s", salida)
	}
}

func TestSinLosExternosSaleConCodigoDos(t *testing.T) {
	codigo, salida := correr("--clave", huellaDeEjemplo(t), filepath.Join("testdata", "pericial.zip"))

	if codigo != 2 || !strings.Contains(salida, "RESULTADO: INCOMPLETO") || !strings.Contains(salida, "[INCOMPLETO]") {
		t.Fatalf("código %d:\n%s", codigo, salida)
	}
}

func TestConOtraHuellaSaleConCodigoUnoYDiceDondeFalla(t *testing.T) {
	otra := "ed25519:" + strings.Repeat("ab", 32)
	codigo, salida := correr("--clave", otra, "--externos", "testdata", filepath.Join("testdata", "pericial.zip"))

	if codigo != 1 || !strings.Contains(salida, "RESULTADO: MANIPULADO") || !strings.Contains(salida, "[FALLA]") {
		t.Fatalf("código %d:\n%s", codigo, salida)
	}
}

func TestUnInvestigativoSeAnunciaComoNoPrueba(t *testing.T) {
	codigo, salida := correr("--clave", huellaDeEjemplo(t), "--externos", "testdata", filepath.Join("testdata", "investigativo.zip"))

	if codigo != 0 || !strings.Contains(salida, "SÓLO INVESTIGACIÓN, NO ES PRUEBA") {
		t.Fatalf("código %d:\n%s", codigo, salida)
	}
}

func TestExigirPericialSobreUnInvestigativoFalla(t *testing.T) {
	codigo, _ := correr("--clave", huellaDeEjemplo(t), "--externos", "testdata", "--exigir", "pericial",
		filepath.Join("testdata", "investigativo.zip"))

	if codigo != 1 {
		t.Fatalf("código %d, se esperaba 1", codigo)
	}
}

func TestLaSalidaJsonEsElResultadoCompleto(t *testing.T) {
	codigo, salida := correr("--json", "--clave", huellaDeEjemplo(t), "--externos", "testdata", filepath.Join("testdata", "pericial.zip"))

	var r struct {
		Estado         string `json:"estado"`
		Tipo           string `json:"tipo"`
		Comprobaciones []struct {
			Nivel string `json:"nivel"`
		} `json:"comprobaciones"`
	}
	if err := json.Unmarshal([]byte(salida), &r); err != nil {
		t.Fatalf("no es JSON: %v\n%s", err, salida)
	}
	if codigo != 0 || r.Estado != "ÍNTEGRO" || r.Tipo != "pericial" || len(r.Comprobaciones) == 0 {
		t.Fatalf("código %d, resultado %+v", codigo, r)
	}
}

func TestErroresDeUsoSalenConCodigoTres(t *testing.T) {
	casos := [][]string{
		{},
		{filepath.Join("testdata", "pericial.zip")},
		{"--clave", "no-es-una-huella", filepath.Join("testdata", "pericial.zip")},
		{"--clave", huellaDeEjemplo(t), "--exigir", "otro", filepath.Join("testdata", "pericial.zip")},
		{"--clave", huellaDeEjemplo(t), filepath.Join("testdata", "no-existe.zip")},
		{"--clave", huellaDeEjemplo(t), "--externos", filepath.Join("testdata", "no-existe"), filepath.Join("testdata", "pericial.zip")},
	}
	for _, args := range casos {
		if codigo, salida := correr(args...); codigo != 3 {
			t.Errorf("%v: código %d, se esperaba 3\n%s", args, codigo, salida)
		}
	}
}

func TestUnArchivoQueNoEsBundleEsManipulado(t *testing.T) {
	codigo, _ := correr("--clave", huellaDeEjemplo(t), filepath.Join("testdata", "huella.txt"))

	if codigo != 1 {
		t.Fatalf("código %d, se esperaba 1", codigo)
	}
}
