package verificacion

import (
	"archive/zip"
	"crypto/ed25519"
	"crypto/sha256"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

type Estado string

const (
	Integro    Estado = "ÍNTEGRO"
	Incompleto Estado = "INCOMPLETO"
	Manipulado Estado = "MANIPULADO"
)

type Nivel string

const (
	Ok       Nivel = "OK"
	Falla    Nivel = "FALLA"
	Faltante Nivel = "INCOMPLETO"
)

// Reglas de docs/formato-bundle-v1.md, «Reglas de verificación».
const (
	ReglaZip        = "zip"
	ReglaManifiesto = "manifiesto"
	ReglaClave      = "clave"
	ReglaFirma      = "firma"
	ReglaArchivos   = "archivos"
	ReglaCadena     = "cadena"
	ReglaSellos     = "sellos"
	ReglaEvidencias = "evidencias"
	ReglaPericial   = "pericial"
)

const (
	manifiesto      = "manifiesto.json"
	firmaManifiesto = "manifiesto.json.sig"
	archivoCadena   = "cadena.json"
	archivoSellos   = "sellos.json"
	maximoJSON      = 256 << 20
	viaAdmisible    = "AdmisibleEnJuicio"
)

// tiposPericiales es la lista blanca cerrada: en un bundle pericial ningún otro tipo puede ser completo.
var tiposPericiales = map[string]bool{
	"CasoAbierto": true, "AnalistaAsignado": true, "CasoCerrado": true, "EvidenciaIngestada": true,
	"EvidenciaAccedida": true, "ControlCruzadoCompletado": true, "OperacionAplicada": true,
	"OperacionReproducida": true, "InformePericialEmitido": true,
}

type Opciones struct {
	// Huella de la clave del laboratorio, obtenida por un medio independiente del bundle.
	Huella string
	// Carpeta donde buscar, por hash, las evidencias que no viajan embebidas.
	Externos string
	// "pericial" o "investigativo": exige ese tipo de bundle.
	ExigirTipo string
}

type Comprobacion struct {
	Nivel     Nivel  `json:"nivel"`
	Regla     string `json:"regla"`
	Mensaje   string `json:"mensaje"`
	Secuencia *int64 `json:"secuencia,omitempty"`
	Ruta      string `json:"ruta,omitempty"`
}

type Resultado struct {
	Estado         Estado         `json:"estado"`
	Tipo           string         `json:"tipo"`
	Caso           string         `json:"caso"`
	Hasta          int64          `json:"hasta"`
	Completos      int            `json:"asientosCompletos"`
	Opacos         int            `json:"asientosOpacos"`
	Comprobaciones []Comprobacion `json:"comprobaciones"`
}

type verificador struct {
	op       Opciones
	huella   string
	res      Resultado
	entradas map[string]*zip.File
	m        Manifiesto
	publica  ed25519.PublicKey // sólo si coincide con la huella de confianza
	asientos map[int64]Asiento
	cadena   []Asiento
}

// Verificar aplica todas las reglas y reporta cada falla que encuentra, no sólo la primera.
func Verificar(r io.ReaderAt, tamano int64, op Opciones) Resultado {
	v := &verificador{op: op, huella: NormalizarHuella(op.Huella), asientos: map[int64]Asiento{}}
	v.verificar(r, tamano)

	v.res.Estado = Integro
	for _, c := range v.res.Comprobaciones {
		switch {
		case c.Nivel == Falla:
			v.res.Estado = Manipulado
		case c.Nivel == Faltante && v.res.Estado == Integro:
			v.res.Estado = Incompleto
		}
	}
	return v.res
}

// VerificarArchivo verifica el bundle en la ruta dada.
func VerificarArchivo(ruta string, op Opciones) (Resultado, error) {
	f, err := os.Open(ruta)
	if err != nil {
		return Resultado{}, err
	}
	defer f.Close()
	info, err := f.Stat()
	if err != nil {
		return Resultado{}, err
	}
	return Verificar(f, info.Size(), op), nil
}

func (v *verificador) agregar(nivel Nivel, regla, ruta string, secuencia *int64, formato string, args ...any) {
	v.res.Comprobaciones = append(v.res.Comprobaciones, Comprobacion{
		Nivel: nivel, Regla: regla, Mensaje: fmt.Sprintf(formato, args...), Secuencia: secuencia, Ruta: ruta,
	})
}

func (v *verificador) ok(regla, formato string, args ...any) {
	v.agregar(Ok, regla, "", nil, formato, args...)
}

func (v *verificador) falla(regla, formato string, args ...any) {
	v.agregar(Falla, regla, "", nil, formato, args...)
}

func (v *verificador) fallaEn(regla string, secuencia int64, formato string, args ...any) {
	v.agregar(Falla, regla, "", &secuencia, formato, args...)
}

func (v *verificador) fallaDe(regla, ruta, formato string, args ...any) {
	v.agregar(Falla, regla, ruta, nil, formato, args...)
}

func (v *verificador) verificar(r io.ReaderAt, tamano int64) {
	zr, err := zip.NewReader(r, tamano)
	if err != nil {
		v.falla(ReglaZip, "No es un ZIP legible: %v", err)
		return
	}
	if !v.leerEntradas(zr) {
		return
	}
	crudo, ok := v.leer(manifiesto, ReglaManifiesto)
	if !ok {
		return
	}
	if err := decodificar(crudo, &v.m); err != nil {
		v.fallaDe(ReglaManifiesto, manifiesto, "El manifiesto no es válido: %v", err)
		return
	}
	if !v.verificarManifiesto() {
		return
	}
	v.verificarClave()
	v.verificarFirma(crudo)
	v.verificarDeclaradas()
	v.verificarArchivos()
	v.verificarEvidenciasPresentes()
	v.verificarCadena()
	v.verificarSellos()
	v.verificarEvidenciasAncladas()
	if v.m.Tipo == "pericial" {
		v.verificarPericial()
	}
}

func rutaSegura(r string) bool {
	if r == "" || strings.HasPrefix(r, "/") || strings.ContainsAny(r, "\\:") {
		return false
	}
	for _, c := range r {
		if c < 0x20 || c == 0x7f {
			return false
		}
	}
	for _, s := range strings.Split(r, "/") {
		if s == "" || s == "." || s == ".." {
			return false
		}
	}
	return true
}

func (v *verificador) leerEntradas(zr *zip.Reader) bool {
	v.entradas = map[string]*zip.File{}
	sanas := true
	for _, f := range zr.File {
		if strings.HasSuffix(f.Name, "/") && f.UncompressedSize64 == 0 {
			continue
		}
		if !rutaSegura(f.Name) {
			v.fallaDe(ReglaZip, f.Name, "La entrada %q tiene una ruta inválida.", f.Name)
			sanas = false
			continue
		}
		if _, repetida := v.entradas[f.Name]; repetida {
			v.fallaDe(ReglaZip, f.Name, "La entrada %q está repetida en el ZIP.", f.Name)
			sanas = false
			continue
		}
		v.entradas[f.Name] = f
	}
	if sanas {
		v.ok(ReglaZip, "ZIP legible, %d entradas con rutas válidas.", len(v.entradas))
	}
	return true
}

// leer trae a memoria un archivo chico del bundle (manifiesto, cadena, sellos, firma).
func (v *verificador) leer(ruta, regla string) ([]byte, bool) {
	f, ok := v.entradas[ruta]
	if !ok {
		v.fallaDe(regla, ruta, "Falta %s en el bundle.", ruta)
		return nil, false
	}
	rc, err := f.Open()
	if err != nil {
		v.fallaDe(regla, ruta, "No se pudo leer %s: %v", ruta, err)
		return nil, false
	}
	defer rc.Close()
	datos, err := io.ReadAll(io.LimitReader(rc, maximoJSON+1))
	if err != nil {
		v.fallaDe(regla, ruta, "No se pudo leer %s: %v", ruta, err)
		return nil, false
	}
	if len(datos) > maximoJSON {
		v.fallaDe(regla, ruta, "%s supera el tamaño máximo admitido.", ruta)
		return nil, false
	}
	return datos, true
}

func (v *verificador) verificarManifiesto() bool {
	m := v.m
	if m.Formato != formatoBundle {
		v.fallaDe(ReglaManifiesto, manifiesto, "Formato %q desconocido: se esperaba %s.", m.Formato, formatoBundle)
		return false
	}
	if m.Tipo != "pericial" && m.Tipo != "investigativo" {
		v.fallaDe(ReglaManifiesto, manifiesto, "Tipo de bundle %q inválido.", m.Tipo)
		return false
	}
	v.res.Tipo, v.res.Caso, v.res.Hasta = m.Tipo, m.Caso.ID, m.Cadena.Hasta
	v.ok(ReglaManifiesto, "Bundle %s %s del caso %s (expediente %s), asientos 1-%d.",
		m.Formato, strings.ToUpper(m.Tipo), m.Caso.ID, m.Caso.NumeroExpediente, m.Cadena.Hasta)
	if v.op.ExigirTipo != "" && v.op.ExigirTipo != m.Tipo {
		v.falla(ReglaManifiesto, "Se exigió un bundle %s, pero este es %s.", v.op.ExigirTipo, m.Tipo)
	}
	return true
}

func (v *verificador) verificarClave() {
	publica, err := hex.DecodeString(v.m.Clave.Publica)
	if err != nil || len(publica) != ed25519.PublicKeySize {
		v.fallaDe(ReglaClave, manifiesto, "La clave pública del manifiesto no es una clave Ed25519.")
		return
	}
	if HuellaDe(publica) != v.huella || v.m.Clave.ID != v.huella {
		v.falla(ReglaClave, "La clave del bundle (%s) no corresponde a la huella de confianza indicada (%s).",
			HuellaDe(publica), v.huella)
		return
	}
	v.publica = publica
	v.ok(ReglaClave, "La clave del bundle es la de la huella indicada (%s).", v.huella)
}

func (v *verificador) verificarFirma(crudo []byte) {
	sig, ok := v.leer(firmaManifiesto, ReglaFirma)
	if !ok {
		return
	}
	if v.publica == nil {
		v.fallaDe(ReglaFirma, firmaManifiesto, "No se puede verificar la firma del manifiesto sin la clave de confianza.")
		return
	}
	texto := strings.TrimSuffix(strings.TrimSuffix(string(sig), "\n"), "\r")
	firma, err := base64.StdEncoding.Strict().DecodeString(texto)
	if err != nil || len(firma) != longitudFirma || !ed25519.Verify(v.publica, crudo, firma) {
		v.fallaDe(ReglaFirma, firmaManifiesto, "La firma del manifiesto no es válida: el manifiesto fue alterado o no lo firmó esta clave.")
		return
	}
	v.ok(ReglaFirma, "Firma del manifiesto válida.")
}

func (v *verificador) verificarDeclaradas() {
	declaradas := map[string]bool{manifiesto: true, firmaManifiesto: true}
	declarar := func(ruta string) {
		if declaradas[ruta] {
			v.fallaDe(ReglaManifiesto, ruta, "La ruta %q está declarada dos veces.", ruta)
		}
		declaradas[ruta] = true
	}
	for _, a := range v.m.Archivos {
		declarar(a.Ruta)
	}
	for _, e := range v.m.Evidencias {
		if e.Embebida && e.Ruta != nil {
			declarar(*e.Ruta)
		}
	}
	sobrantes := 0
	for _, ruta := range ordenadas(v.entradas) {
		if !declaradas[ruta] {
			v.fallaDe(ReglaZip, ruta, "La entrada %q no está declarada en el manifiesto.", ruta)
			sobrantes++
		}
	}
	if sobrantes == 0 {
		v.ok(ReglaZip, "Toda entrada del ZIP está declarada en el manifiesto.")
	}
}

func ordenadas(m map[string]*zip.File) []string {
	rutas := make([]string, 0, len(m))
	for r := range m {
		rutas = append(rutas, r)
	}
	sort.Strings(rutas)
	return rutas
}

// hashDeEntrada hashea una entrada en streaming: las evidencias pueden ser grandes.
func (v *verificador) hashDeEntrada(ruta string) (string, int64, error) {
	rc, err := v.entradas[ruta].Open()
	if err != nil {
		return "", 0, err
	}
	defer rc.Close()
	h := sha256.New()
	n, err := io.Copy(h, rc)
	if err != nil {
		return "", 0, err
	}
	return hex.EncodeToString(h.Sum(nil)), n, nil
}

// comprobarEntrada verifica que la ruta exista con ese hash y ese tamaño.
func (v *verificador) comprobarEntrada(ruta, sha string, tamano int64) bool {
	if _, ok := v.entradas[ruta]; !ok {
		v.fallaDe(ReglaArchivos, ruta, "Falta %s, declarado en el manifiesto.", ruta)
		return false
	}
	calculado, n, err := v.hashDeEntrada(ruta)
	switch {
	case err != nil:
		v.fallaDe(ReglaArchivos, ruta, "No se pudo leer %s: %v", ruta, err)
	case calculado != sha:
		v.fallaDe(ReglaArchivos, ruta, "El hash de %s no coincide con el manifiesto (%s, se declaró %s).", ruta, calculado, sha)
	case n != tamano:
		v.fallaDe(ReglaArchivos, ruta, "El tamaño de %s no coincide con el manifiesto (%d bytes, se declararon %d).", ruta, n, tamano)
	default:
		return true
	}
	return false
}

func (v *verificador) verificarArchivos() {
	buenos := 0
	for _, requerido := range []string{archivoCadena, archivoSellos} {
		declarado := false
		for _, a := range v.m.Archivos {
			declarado = declarado || a.Ruta == requerido
		}
		if !declarado {
			v.fallaDe(ReglaArchivos, requerido, "El manifiesto no declara %s.", requerido)
		}
	}
	for _, a := range v.m.Archivos {
		if !rutaSegura(a.Ruta) || strings.HasPrefix(a.Ruta, "evidencias/") {
			v.fallaDe(ReglaArchivos, a.Ruta, "La ruta %q no es válida para un archivo del bundle.", a.Ruta)
			continue
		}
		if v.comprobarEntrada(a.Ruta, a.Sha256, a.TamanoBytes) {
			buenos++
		}
	}
	if buenos == len(v.m.Archivos) {
		v.ok(ReglaArchivos, "Archivos del bundle: %d/%d coinciden con el manifiesto.", buenos, len(v.m.Archivos))
	}
}

func (v *verificador) verificarEvidenciasPresentes() {
	embebidas, buenas := 0, 0
	var externas []Evidencia
	for _, e := range v.m.Evidencias {
		if !esHash(e.Sha256) {
			v.falla(ReglaEvidencias, "La evidencia %s tiene un hash inválido.", e.ID)
			continue
		}
		if !e.Embebida {
			if e.Ruta != nil {
				v.falla(ReglaEvidencias, "La evidencia %s no está embebida pero declara una ruta.", e.ID)
			}
			externas = append(externas, e)
			continue
		}
		embebidas++
		if e.Ruta == nil || !rutaSegura(*e.Ruta) || !strings.HasPrefix(*e.Ruta, "evidencias/") {
			v.falla(ReglaEvidencias, "La evidencia embebida %s no tiene una ruta válida bajo evidencias/.", e.ID)
			continue
		}
		if v.comprobarEntrada(*e.Ruta, e.Sha256, e.TamanoBytes) {
			buenas++
		}
	}
	if buenas == embebidas && embebidas > 0 {
		v.ok(ReglaArchivos, "Evidencias embebidas: %d/%d coinciden con el manifiesto.", buenas, embebidas)
	}
	v.buscarExternas(externas)
}

// buscarExternas busca cada evidencia externa por su hash (no por su nombre) en la carpeta indicada.
// Sólo hashea los archivos cuyo tamaño coincide con alguna pendiente.
func (v *verificador) buscarExternas(externas []Evidencia) {
	if len(externas) == 0 {
		return
	}
	encontradas := map[string]string{}
	if v.op.Externos != "" {
		porTamano := map[int64]bool{}
		for _, e := range externas {
			porTamano[e.TamanoBytes] = true
		}
		archivos, err := os.ReadDir(v.op.Externos)
		if err != nil {
			v.agregar(Faltante, ReglaArchivos, "", nil, "No se pudo leer la carpeta de externos %s: %v", v.op.Externos, err)
		}
		for _, a := range archivos {
			info, err := a.Info()
			if err != nil || !info.Mode().IsRegular() || !porTamano[info.Size()] {
				continue
			}
			ruta := filepath.Join(v.op.Externos, a.Name())
			if h, err := hashDeArchivo(ruta); err == nil {
				encontradas[h] = ruta
			}
		}
	}
	for _, e := range externas {
		if ruta, ok := encontradas[e.Sha256]; ok {
			v.agregar(Ok, ReglaArchivos, ruta, nil, "Evidencia externa %s (%s): encontrada en %s y coincide.", e.ID, e.Nombre, ruta)
			continue
		}
		v.agregar(Faltante, ReglaArchivos, "", nil,
			"Evidencia externa %s (%q, %d bytes, sha256 %s): no está en la carpeta de externos.", e.ID, e.Nombre, e.TamanoBytes, e.Sha256)
	}
}

func hashDeArchivo(ruta string) (string, error) {
	f, err := os.Open(ruta)
	if err != nil {
		return "", err
	}
	defer f.Close()
	h := sha256.New()
	if _, err := io.Copy(h, f); err != nil {
		return "", err
	}
	return hex.EncodeToString(h.Sum(nil)), nil
}

var (
	clavesDeOpaco    = []string{"secuencia", "opaco", "hashAnterior", "hash"}
	clavesDeCompleto = []string{"secuencia", "id", "tipo", "evidencia", "actor", "timestamp", "detalle", "hashAnterior", "hash"}
)

func mismasClaves(m map[string]json.RawMessage, esperadas []string) bool {
	if len(m) != len(esperadas) {
		return false
	}
	for _, c := range esperadas {
		if _, ok := m[c]; !ok {
			return false
		}
	}
	return true
}

func (v *verificador) leerCadena() bool {
	crudo, ok := v.leer(archivoCadena, ReglaCadena)
	if !ok {
		return false
	}
	var c struct {
		Formato  string            `json:"formato"`
		Caso     string            `json:"caso"`
		Asientos []json.RawMessage `json:"asientos"`
	}
	if err := decodificar(crudo, &c); err != nil {
		v.fallaDe(ReglaCadena, archivoCadena, "cadena.json no es válido: %v", err)
		return false
	}
	if c.Formato != formatoCadena || c.Caso != v.m.Caso.ID {
		v.fallaDe(ReglaCadena, archivoCadena, "cadena.json no es una cadena %s del caso %s.", formatoCadena, v.m.Caso.ID)
		return false
	}
	for i, raw := range c.Asientos {
		var claves map[string]json.RawMessage
		var a Asiento
		err := json.Unmarshal(raw, &claves)
		if err == nil {
			err = decodificar(raw, &a)
		}
		switch {
		case err != nil:
			v.fallaDe(ReglaCadena, archivoCadena, "El asiento %d de cadena.json no es válido: %v", i+1, err)
			return false
		case a.Opaco && !mismasClaves(claves, clavesDeOpaco):
			v.fallaEn(ReglaCadena, a.Secuencia, "El asiento opaco %d debe traer sólo secuencia, opaco, hashAnterior y hash.", a.Secuencia)
			return false
		case !a.Opaco && !mismasClaves(claves, clavesDeCompleto):
			v.fallaEn(ReglaCadena, a.Secuencia, "Al asiento %d le faltan campos o le sobran.", a.Secuencia)
			return false
		}
		v.cadena = append(v.cadena, a)
	}
	return true
}

func (v *verificador) verificarCadena() {
	if !v.leerCadena() {
		return
	}
	anterior, sanos := hashGenesis, true
	for i, a := range v.cadena {
		esperada := int64(i + 1)
		if a.Secuencia != esperada {
			v.fallaEn(ReglaCadena, a.Secuencia, "Asiento con secuencia %d donde se esperaba %d: falta o sobra un asiento.", a.Secuencia, esperada)
			return
		}
		v.asientos[a.Secuencia] = a
		if !esHash(a.Hash) || !esHash(a.HashAnterior) {
			v.fallaEn(ReglaCadena, a.Secuencia, "El asiento %d tiene un hash inválido.", a.Secuencia)
			return
		}
		if a.HashAnterior != anterior {
			v.fallaEn(ReglaCadena, a.Secuencia, "El hash anterior del asiento %d no coincide con el asiento previo.", a.Secuencia)
			sanos = false
		}
		if a.Opaco {
			v.res.Opacos++
		} else {
			v.res.Completos++
			if HashDeAsiento(v.m.Caso.ID, a) != a.Hash {
				v.fallaEn(ReglaCadena, a.Secuencia, "El contenido del asiento %d no coincide con su hash: fue alterado.", a.Secuencia)
				sanos = false
			}
		}
		anterior = a.Hash
	}
	if int64(len(v.cadena)) != v.m.Cadena.Hasta || anterior != v.m.Cadena.HashCabeza {
		v.falla(ReglaCadena, "La cadena termina en la secuencia %d con hash %s; el manifiesto declara %d y %s.",
			len(v.cadena), anterior, v.m.Cadena.Hasta, v.m.Cadena.HashCabeza)
		sanos = false
	}
	if sanos {
		v.ok(ReglaCadena, "Cadena: %d asientos enlazados; %d completos con hash recalculado, %d opacos (sólo enlace).",
			len(v.cadena), v.res.Completos, v.res.Opacos)
	}
}

func (v *verificador) verificarSellos() {
	crudo, ok := v.leer(archivoSellos, ReglaSellos)
	if !ok {
		return
	}
	var s Sellos
	if err := decodificar(crudo, &s); err != nil {
		v.fallaDe(ReglaSellos, archivoSellos, "sellos.json no es válido: %v", err)
		return
	}
	if s.Formato != formatoSellos {
		v.fallaDe(ReglaSellos, archivoSellos, "sellos.json no tiene el formato %s.", formatoSellos)
		return
	}
	if len(s.Sellos) == 0 {
		v.falla(ReglaSellos, "El bundle no trae ningún sello de la cadena.")
		return
	}
	sort.SliceStable(s.Sellos, func(i, j int) bool { return s.Sellos[i].Secuencia < s.Sellos[j].Secuencia })
	sanos := true
	var secuencias []string
	for _, sello := range s.Sellos {
		secuencias = append(secuencias, fmt.Sprint(sello.Secuencia))
		seq := sello.Secuencia
		asiento, existe := v.asientos[seq]
		switch {
		case sello.Caso != v.m.Caso.ID:
			v.fallaEn(ReglaSellos, seq, "El sello de la secuencia %d es de otro caso (%s).", seq, sello.Caso)
		case sello.ClaveID != v.huella:
			v.fallaEn(ReglaSellos, seq, "El sello de la secuencia %d es de otra clave (%s).", seq, sello.ClaveID)
		case v.publica == nil:
			v.fallaEn(ReglaSellos, seq, "No se puede verificar la firma del sello de la secuencia %d sin la clave de confianza.", seq)
		case !FirmaDeSelloValida(sello, v.publica):
			v.fallaEn(ReglaSellos, seq, "La firma del sello de la secuencia %d no es válida: el sello fue alterado.", seq)
		case seq < 1 || seq > int64(len(v.cadena)) || !existe:
			v.fallaEn(ReglaSellos, seq, "El sello cubre la secuencia %d, pero la cadena tiene %d asientos: fue truncada.", seq, len(v.cadena))
		case asiento.Hash != sello.HashCabeza:
			v.fallaEn(ReglaSellos, seq,
				"El asiento %d no tiene el hash de su sello (%s): la cadena fue reconstruida o alterada.", seq, sello.HashCabeza)
		default:
			continue
		}
		sanos = false
	}
	if ultimo := s.Sellos[len(s.Sellos)-1].Secuencia; ultimo != v.m.Cadena.Hasta {
		v.falla(ReglaSellos, "El último sello (secuencia %d) no cubre la cabeza de la cadena (secuencia %d).", ultimo, v.m.Cadena.Hasta)
		sanos = false
	}
	if sanos {
		v.ok(ReglaSellos, "Sellos válidos: %d (secuencias %s); coinciden con la cadena y el último cubre la cabeza.",
			len(s.Sellos), strings.Join(secuencias, ", "))
	}
}

func texto(m map[string]any, ruta ...string) string {
	var actual any = m
	for _, r := range ruta {
		obj, ok := actual.(map[string]any)
		if !ok {
			return ""
		}
		actual = obj[r]
	}
	s, _ := actual.(string)
	return s
}

func numero(m map[string]any, ruta ...string) (int64, bool) {
	var actual any = m
	for _, r := range ruta {
		obj, ok := actual.(map[string]any)
		if !ok {
			return 0, false
		}
		actual = obj[r]
	}
	n, ok := actual.(json.Number)
	if !ok {
		return 0, false
	}
	i, err := n.Int64()
	return i, err == nil
}

func (v *verificador) verificarEvidenciasAncladas() {
	if len(v.asientos) == 0 {
		return
	}
	porID := map[string]Evidencia{}
	for _, e := range v.m.Evidencias {
		if _, repetida := porID[e.ID]; repetida {
			v.falla(ReglaEvidencias, "La evidencia %s figura dos veces en el manifiesto.", e.ID)
		}
		porID[e.ID] = e
	}
	sanas := 0
	for _, e := range v.m.Evidencias {
		if v.anclar(e, porID) {
			sanas++
		}
	}
	if sanas == len(v.m.Evidencias) {
		v.ok(ReglaEvidencias, "Evidencias: las %d tienen su asiento de origen en la cadena y coinciden con él.", sanas)
	}
}

func (v *verificador) anclar(e Evidencia, porID map[string]Evidencia) bool {
	seq := e.AsientoDeOrigen
	a, ok := v.asientos[seq]
	if !ok {
		v.fallaEn(ReglaEvidencias, seq, "La evidencia %s declara el asiento de origen %d, que no está en la cadena.", e.ID, seq)
		return false
	}
	if a.Opaco {
		v.fallaEn(ReglaEvidencias, seq, "El asiento de origen %d de la evidencia %s es opaco: no puede presentarse.", seq, e.ID)
		return false
	}
	if a.Evidencia == nil || *a.Evidencia != e.ID {
		v.fallaEn(ReglaEvidencias, seq, "El asiento %d no registra la evidencia %s.", seq, e.ID)
		return false
	}
	d, err := decodificarDetalle(a.Detalle)
	if err != nil {
		v.fallaEn(ReglaEvidencias, seq, "El detalle del asiento %d de la evidencia %s no es válido: %v", seq, e.ID, err)
		return false
	}

	var sha string
	var tamano int64
	var tieneTamano bool
	switch {
	case a.Tipo == "EvidenciaIngestada":
		sha = texto(d, "sha256")
		tamano, tieneTamano = numero(d, "tamanoBytes")
	case a.Tipo == "OperacionAplicada" || a.Tipo == "OperacionIaAplicada" && v.m.Tipo == "investigativo":
		if texto(d, "salida", "evidenciaId") != e.ID {
			v.fallaEn(ReglaEvidencias, seq, "El asiento %d no produjo la evidencia %s.", seq, e.ID)
			return false
		}
		sha = texto(d, "salida", "sha256")
		tamano, tieneTamano = numero(d, "salida", "tamanoBytes")
		entrada, ok := porID[texto(d, "entrada", "evidenciaId")]
		if !ok || entrada.Sha256 != texto(d, "entrada", "sha256") || entrada.AsientoDeOrigen >= seq {
			v.fallaEn(ReglaEvidencias, seq, "La entrada del derivado %s no está en el bundle: su linaje no llega a un original.", e.ID)
			return false
		}
	default:
		v.fallaEn(ReglaEvidencias, seq, "La evidencia %s declara como origen un asiento %s, que no registra evidencias.", e.ID, a.Tipo)
		return false
	}
	if sha != e.Sha256 || !tieneTamano || tamano != e.TamanoBytes {
		v.fallaEn(ReglaEvidencias, seq, "La evidencia %s no coincide con su asiento de origen %d (hash o tamaño distintos).", e.ID, seq)
		return false
	}
	if v.m.Tipo == "pericial" {
		if via := texto(d, "via"); via != viaAdmisible {
			v.fallaEn(ReglaPericial, seq, "La evidencia %s tiene vía %q en su asiento %d, no %s: no puede ir en un informe pericial.",
				e.ID, via, seq, viaAdmisible)
			return false
		}
	}
	return true
}

func (v *verificador) verificarPericial() {
	if len(v.asientos) == 0 {
		return
	}
	enElInforme := map[string]bool{}
	for _, e := range v.m.Evidencias {
		enElInforme[e.ID] = true
	}
	sano := true
	for _, a := range v.cadena {
		if a.Opaco {
			continue
		}
		if !tiposPericiales[a.Tipo] {
			v.fallaEn(ReglaPericial, a.Secuencia, "El asiento %d es completo y de tipo %s, que no puede figurar en un informe pericial.", a.Secuencia, a.Tipo)
			sano = false
			continue
		}
		if a.Evidencia != nil && !enElInforme[*a.Evidencia] {
			v.fallaEn(ReglaPericial, a.Secuencia,
				"El asiento %d es completo y refiere a la evidencia %s, que no está en el informe pericial.", a.Secuencia, *a.Evidencia)
			sano = false
		}
	}
	if sano {
		v.ok(ReglaPericial, "Pericial: ningún asiento completo es de IA ni de una evidencia excluida; %d asientos opacos.", v.res.Opacos)
	}
}
