package verificacion

import (
	"crypto/ed25519"
	"crypto/sha256"
	"encoding/base64"
	"encoding/hex"
	"strconv"
	"strings"
)

const (
	formatoBundle  = "forensic-video/bundle/v1"
	formatoCadena  = "forensic-video/custodia/v1"
	formatoSellos  = "forensic-video/sello/v1"
	hashGenesis    = "0000000000000000000000000000000000000000000000000000000000000000"
	longitudFirma  = ed25519.SignatureSize
	prefijoDeClave = "ed25519:"
)

// campos arma la codificación canónica: cada campo como «nombre:longitud en bytes:valor\n».
func campos(pares ...string) []byte {
	var b strings.Builder
	for i := 0; i+1 < len(pares); i += 2 {
		b.WriteString(pares[i])
		b.WriteByte(':')
		b.WriteString(strconv.Itoa(len(pares[i+1])))
		b.WriteByte(':')
		b.WriteString(pares[i+1])
		b.WriteByte('\n')
	}
	return []byte(b.String())
}

// HashDeAsiento recalcula el hash de un asiento completo del caso.
func HashDeAsiento(caso string, a Asiento) string {
	evidencia := ""
	if a.Evidencia != nil {
		evidencia = *a.Evidencia
	}
	h := sha256.Sum256(campos(
		"formato", formatoCadena,
		"id", a.ID,
		"caso", caso,
		"secuencia", strconv.FormatInt(a.Secuencia, 10),
		"tipo", a.Tipo,
		"evidencia", evidencia,
		"actor", a.Actor,
		"timestamp", a.Timestamp,
		"anterior", a.HashAnterior,
		"detalle", a.Detalle,
	))
	return hex.EncodeToString(h[:])
}

// MensajeDeSello son los bytes que firma el laboratorio.
func MensajeDeSello(s Sello) []byte {
	return campos(
		"formato", formatoSellos,
		"caso", s.Caso,
		"secuencia", strconv.FormatInt(s.Secuencia, 10),
		"cabeza", s.HashCabeza,
		"sellado", s.SelladoEn,
		"clave", s.ClaveID,
	)
}

// FirmaDeSelloValida verifica la firma Ed25519 del sello con la clave pública dada.
func FirmaDeSelloValida(s Sello, publica ed25519.PublicKey) bool {
	firma, err := base64.StdEncoding.Strict().DecodeString(s.Firma)
	return err == nil && len(firma) == longitudFirma && len(publica) == ed25519.PublicKeySize &&
		ed25519.Verify(publica, MensajeDeSello(s), firma)
}

// HuellaDe es el id de una clave pública: «ed25519:» y el SHA-256 de sus 32 bytes.
func HuellaDe(publica ed25519.PublicKey) string {
	h := sha256.Sum256(publica)
	return prefijoDeClave + hex.EncodeToString(h[:])
}

// NormalizarHuella acepta la huella con o sin el prefijo «ed25519:» y en cualquier caja.
func NormalizarHuella(h string) string {
	h = strings.ToLower(strings.TrimSpace(h))
	if !strings.HasPrefix(h, prefijoDeClave) {
		h = prefijoDeClave + h
	}
	return h
}

func esHash(h string) bool {
	if len(h) != 64 {
		return false
	}
	for _, c := range h {
		if !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f') {
			return false
		}
	}
	return true
}
