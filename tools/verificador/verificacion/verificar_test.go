package verificacion

import (
	"bytes"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func verificar(t *testing.T, bundle []byte, op Opciones) Resultado {
	t.Helper()
	if op.Huella == "" {
		op.Huella = huellaDe(claveDelLaboratorio)
	}
	return Verificar(bytes.NewReader(bundle), int64(len(bundle)), op)
}

// externos crea una carpeta con el video que viaja aparte, bajo cualquier nombre.
func externos(t *testing.T, contenido []byte) string {
	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, "lo que sea.mp4"), contenido, 0o600); err != nil {
		t.Fatal(err)
	}
	return dir
}

func fallas(r Resultado) []Comprobacion {
	var f []Comprobacion
	for _, c := range r.Comprobaciones {
		if c.Nivel == Falla {
			f = append(f, c)
		}
	}
	return f
}

// exigirFalla comprueba que el bundle quedó MANIPULADO por la regla indicada y cuyo mensaje dice algo.
func exigirFalla(t *testing.T, r Resultado, regla string, dice string) Comprobacion {
	t.Helper()
	if r.Estado != Manipulado {
		t.Fatalf("estado %s, se esperaba MANIPULADO; comprobaciones: %+v", r.Estado, r.Comprobaciones)
	}
	for _, c := range fallas(r) {
		if c.Regla == regla && strings.Contains(c.Mensaje, dice) {
			return c
		}
	}
	t.Fatalf("no hay una falla de la regla %q que diga %q; fallas: %+v", regla, dice, fallas(r))
	return Comprobacion{}
}

func TestUnBundlePericialIntegroConSuExternoVerifica(t *testing.T) {
	r := verificar(t, nuevoPericial(t).armar(), Opciones{Externos: externos(t, bytesVideo)})

	if r.Estado != Integro {
		t.Fatalf("estado %s; fallas: %+v", r.Estado, fallas(r))
	}
	if r.Tipo != "pericial" || r.Caso != casoDePrueba || r.Hasta != 7 {
		t.Fatalf("resumen inesperado: %+v", r)
	}
	if r.Opacos != 2 || r.Completos != 5 {
		t.Fatalf("se esperaban 5 asientos completos y 2 opacos: %d y %d", r.Completos, r.Opacos)
	}
}

func TestSinLaCarpetaDeExternosElBundleQuedaIncompletoNoManipulado(t *testing.T) {
	r := verificar(t, nuevoPericial(t).armar(), Opciones{})

	if r.Estado != Incompleto {
		t.Fatalf("estado %s; comprobaciones: %+v", r.Estado, r.Comprobaciones)
	}
	if len(fallas(r)) != 0 {
		t.Fatalf("no debería haber fallas: %+v", fallas(r))
	}
}

func TestUnExternoAusenteDeLaCarpetaQuedaIncompleto(t *testing.T) {
	r := verificar(t, nuevoPericial(t).armar(), Opciones{Externos: externos(t, []byte("otro video"))})

	if r.Estado != Incompleto {
		t.Fatalf("estado %s; comprobaciones: %+v", r.Estado, r.Comprobaciones)
	}
}

func TestUnaEvidenciaEmbebidaAlteradaSeSenalaPorSuRuta(t *testing.T) {
	c := nuevoPericial(t)
	ruta := "evidencias/" + sha(bytesDerivado)
	c.alZip = func(e map[string][]byte) { e[ruta] = []byte("png del recorte, retocado") }

	f := exigirFalla(t, verificar(t, c.armar(), Opciones{Externos: externos(t, bytesVideo)}), ReglaArchivos, "hash")
	if f.Ruta != ruta {
		t.Fatalf("la falla debería señalar %s: %+v", ruta, f)
	}
}

func TestUnArchivoAlteradoSeSenala(t *testing.T) {
	c := nuevoPericial(t)
	c.alZip = func(e map[string][]byte) { e["informe.pdf"] = []byte("%PDF-1.7 otro informe") }

	f := exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaArchivos, "hash")
	if f.Ruta != "informe.pdf" {
		t.Fatalf("la falla debería señalar informe.pdf: %+v", f)
	}
}

func TestUnDetalleAlteradoSeSenalaEnSuAsiento(t *testing.T) {
	c := nuevoPericial(t)
	c.despuesDeHashear = func(a []Asiento) { a[2].Detalle = strings.Replace(a[2].Detalle, `"x":1`, `"x":0`, 1) }

	f := exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaCadena, "no coincide con su hash")
	if f.Secuencia == nil || *f.Secuencia != 3 {
		t.Fatalf("la falla debería señalar el asiento 3: %+v", f)
	}
}

func TestUnaCadenaReconstruidaConManifiestoBienFirmadoLaDelatanLosSellos(t *testing.T) {
	// El servidor (con la clave) emite el bundle desde una base reconstruida: todo verifica salvo
	// que los sellos, guardados aparte, son de la cadena original.
	c := nuevoPericial(t)
	original := c.sellos(c.asientos())
	c.antesDeHashear = func(c *constructor) { c.fuentes[1].actor = "impostor" }
	c.alSellar = func([]Sello) []Sello { return original }

	r := verificar(t, c.armar(), Opciones{Externos: externos(t, bytesVideo)})

	f := exigirFalla(t, r, ReglaSellos, "reconstruida")
	if f.Secuencia == nil || *f.Secuencia != 3 {
		t.Fatalf("la falla debería señalar el sello de la secuencia 3: %+v", f)
	}
	for _, x := range fallas(r) {
		if x.Regla == ReglaCadena || x.Regla == ReglaFirma {
			t.Fatalf("la cadena reconstruida verifica por sí misma; sólo deberían fallar los sellos: %+v", x)
		}
	}
}

func TestUnaCadenaTruncadaNoTieneElSelloDeSuCabeza(t *testing.T) {
	c := nuevoPericial(t)
	completos := c.sellos(c.asientos())
	c.antesDeHashear = func(c *constructor) {
		c.fuentes = c.fuentes[:6]
		c.sellarEn = []int64{3}
	}
	c.alSellar = func([]Sello) []Sello { return completos }

	exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaSellos, "truncada")
}

func TestUnBundleSinSelloDeLaCabezaNoVerifica(t *testing.T) {
	c := nuevoPericial(t)
	c.sellarEn = []int64{3}

	exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaSellos, "cabeza")
}

func TestUnBundleSinSellosNoVerifica(t *testing.T) {
	c := nuevoPericial(t)
	c.sellarEn = nil

	exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaSellos, "ningún sello")
}

func TestUnSelloAdulteradoRompeSuFirma(t *testing.T) {
	c := nuevoPericial(t)
	c.alSellar = func(s []Sello) []Sello {
		s[0].SelladoEn = "2026-10-01T00:00:00.000000Z"
		return s
	}

	exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaSellos, "firma")
}

func TestUnManifiestoFirmadoConOtraClaveNoVerifica(t *testing.T) {
	c := nuevoPericial(t)
	c.clave = otraClave

	exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaClave, "huella")
}

func TestLaHuellaDeConfianzaLaDaElUsuarioNoElBundle(t *testing.T) {
	r := verificar(t, nuevoPericial(t).armar(), Opciones{Huella: huellaDe(otraClave)})

	exigirFalla(t, r, ReglaClave, "huella")
}

func TestLaClavePublicaDelManifiestoTieneQueSerLaDeLaHuella(t *testing.T) {
	c := nuevoPericial(t)
	c.alManifiesto = func(m *Manifiesto) { m.Clave.ID = huellaDe(claveDelLaboratorio) }
	c.clave = otraClave

	exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaClave, "huella")
}

func TestUnManifiestoEditadoDespuesDeFirmarNoVerifica(t *testing.T) {
	c := nuevoPericial(t)
	c.alZip = func(e map[string][]byte) {
		e["manifiesto.json"] = bytes.Replace(e["manifiesto.json"], []byte(`"ana"`), []byte(`"eva"`), 1)
	}

	exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaFirma, "firma")
}

func TestUnaEntradaNoDeclaradaEnElManifiestoEsUnaFalla(t *testing.T) {
	c := nuevoPericial(t)
	c.alZip = func(e map[string][]byte) { e["evidencias/colada.png"] = []byte("no figura en el manifiesto") }

	f := exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaZip, "no está declarada")
	if f.Ruta != "evidencias/colada.png" {
		t.Fatalf("la falla debería señalar la entrada: %+v", f)
	}
}

func TestUnaRutaInseguraEsUnaFalla(t *testing.T) {
	c := nuevoPericial(t)
	c.alZip = func(e map[string][]byte) { e["../fuera.txt"] = []byte("x") }

	exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaZip, "ruta")
}

func TestUnArchivoDeclaradoQueFaltaEsUnaFalla(t *testing.T) {
	c := nuevoPericial(t)
	c.alZip = func(e map[string][]byte) { delete(e, "LEEME.txt") }

	f := exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaArchivos, "Falta")
	if f.Ruta != "LEEME.txt" {
		t.Fatalf("la falla debería señalar LEEME.txt: %+v", f)
	}
}

func TestUnOpacoQueNoEncadenaSeDetecta(t *testing.T) {
	c := nuevoPericial(t)
	c.despuesDeHashear = func(a []Asiento) { a[3].Hash = strings.Repeat("e", 64) }

	f := exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaCadena, "anterior")
	if f.Secuencia == nil || *f.Secuencia != 5 {
		t.Fatalf("la falla debería señalar el asiento 5, que no encadena con el opaco: %+v", f)
	}
}

func TestUnHuecoEnLaCadenaSeDetecta(t *testing.T) {
	c := nuevoPericial(t)
	c.despuesDeHashear = func(a []Asiento) { a[5].Secuencia = 9 }

	exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaCadena, "secuencia")
}

func TestUnPericialConUnAsientoDeIaCompletoNoVerifica(t *testing.T) {
	c := nuevoPericial(t)
	c.fuentes[3].opaco = false

	f := exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaPericial, "HallazgosRegistrados")
	if f.Secuencia == nil || *f.Secuencia != 4 {
		t.Fatalf("la falla debería señalar el asiento 4: %+v", f)
	}
}

func TestUnPericialConUnDerivadoSoloInvestigacionNoVerifica(t *testing.T) {
	c := nuevoPericial(t)
	c.fuentes[2].detalle = detalleOperacion("forensic-video/operacion/v1", idOriginal, bytesOriginal, idDerivado, bytesDerivado, viaInvestigacion)

	exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaPericial, "AdmisibleEnJuicio")
}

func TestUnPericialQueEmbebeElDerivadoDeIaNoVerifica(t *testing.T) {
	c := nuevoPericial(t)
	c.embeber(idDerivadoIa, bytesDerivadoIa, 5)

	exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaEvidencias, "opaco")
}

func TestUnPericialConUnAsientoCompletoDeUnaEvidenciaExcluidaNoVerifica(t *testing.T) {
	c := nuevoPericial(t)
	c.fuentes = append(c.fuentes, asientoFuente{
		tipo: "EvidenciaAccedida", evidencia: ptr(idDerivadoIa), actor: "ana", detalle: `{"integridadVerificada":true}`,
	})
	c.sellarEn = []int64{3, 8}

	exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaPericial, idDerivadoIa)
}

func TestUnaEvidenciaQueNoCoincideConSuAsientoDeOrigenNoVerifica(t *testing.T) {
	c := nuevoPericial(t)
	c.alManifiesto = func(m *Manifiesto) { m.Evidencias[1].AsientoDeOrigen = 2 }

	exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaEvidencias, idDerivado)
}

func TestUnDerivadoCuyaEntradaNoEstaEnElBundleNoVerifica(t *testing.T) {
	c := nuevoPericial(t)
	c.alManifiesto = func(m *Manifiesto) { m.Evidencias = m.Evidencias[1:] }
	c.contenidos = map[string][]byte{"evidencias/" + sha(bytesDerivado): bytesDerivado}

	exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaEvidencias, "entrada")
}

func TestUnBundleInvestigativoIntegroVerificaComoInvestigativo(t *testing.T) {
	r := verificar(t, nuevoInvestigativo(t).armar(), Opciones{Externos: externos(t, bytesVideo)})

	if r.Estado != Integro || r.Tipo != "investigativo" || r.Opacos != 0 {
		t.Fatalf("resultado inesperado: %+v; fallas: %+v", r, fallas(r))
	}
}

func TestUnInvestigativoNoPasaPorPericial(t *testing.T) {
	r := verificar(t, nuevoInvestigativo(t).armar(), Opciones{Externos: externos(t, bytesVideo), ExigirTipo: "pericial"})

	exigirFalla(t, r, ReglaManifiesto, "investigativo")
}

func TestUnJsonConClavesRepetidasEsInvalido(t *testing.T) {
	c := nuevoPericial(t)
	c.manifiestoCrudo = func(b []byte) []byte {
		return bytes.Replace(b, []byte(`"tipo": "pericial",`), []byte(`"tipo": "pericial", "Tipo": "investigativo",`), 1)
	}

	exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaManifiesto, "repetida")
}

func TestUnaClaveDesconocidaEnElManifiestoEsInvalida(t *testing.T) {
	c := nuevoPericial(t)
	c.manifiestoCrudo = func(b []byte) []byte {
		return bytes.Replace(b, []byte(`"tipo": "pericial",`), []byte(`"tipo": "pericial", "admisible": true,`), 1)
	}

	exigirFalla(t, verificar(t, c.armar(), Opciones{}), ReglaManifiesto, "admisible")
}

func TestUnArchivoQueNoEsUnZipEsUnaFalla(t *testing.T) {
	exigirFalla(t, verificar(t, []byte("no soy un zip"), Opciones{}), ReglaZip, "ZIP")
}

func TestSeReportanTodasLasFallasNoSoloLaPrimera(t *testing.T) {
	c := nuevoPericial(t)
	c.despuesDeHashear = func(a []Asiento) { a[0].Actor = "eva" }
	c.alZip = func(e map[string][]byte) { e["informe.pdf"] = []byte("otro") }

	r := verificar(t, c.armar(), Opciones{})

	exigirFalla(t, r, ReglaCadena, "no coincide con su hash")
	exigirFalla(t, r, ReglaArchivos, "hash")
}
