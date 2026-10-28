// Package verificacion verifica un bundle forensic-video/bundle/v1 (docs/formato-bundle-v1.md) sin
// el servidor ni su código: es una reimplementación independiente del formato.
package verificacion

import "encoding/json"

// Manifiesto es manifiesto.json.
type Manifiesto struct {
	Formato     string              `json:"formato"`
	Tipo        string              `json:"tipo"`
	Caso        Caso                `json:"caso"`
	GeneradoEn  string              `json:"generadoEn"`
	GeneradoPor string              `json:"generadoPor"`
	Clave       Clave               `json:"clave"`
	Cadena      CadenaDelManifiesto `json:"cadena"`
	Archivos    []Archivo           `json:"archivos"`
	Evidencias  []Evidencia         `json:"evidencias"`
}

type Caso struct {
	ID               string `json:"id"`
	NumeroExpediente string `json:"numeroExpediente"`
	Descripcion      string `json:"descripcion"`
}

type Clave struct {
	ID      string `json:"id"`
	Publica string `json:"publica"`
}

type CadenaDelManifiesto struct {
	Hasta      int64  `json:"hasta"`
	HashCabeza string `json:"hashCabeza"`
}

type Archivo struct {
	Ruta        string `json:"ruta"`
	Sha256      string `json:"sha256"`
	TamanoBytes int64  `json:"tamanoBytes"`
}

type Evidencia struct {
	ID              string  `json:"id"`
	Sha256          string  `json:"sha256"`
	TamanoBytes     int64   `json:"tamanoBytes"`
	Nombre          string  `json:"nombre"`
	AsientoDeOrigen int64   `json:"asientoDeOrigen"`
	Embebida        bool    `json:"embebida"`
	Ruta            *string `json:"ruta"`
}

// Cadena es cadena.json.
type Cadena struct {
	Formato  string    `json:"formato"`
	Caso     string    `json:"caso"`
	Asientos []Asiento `json:"asientos"`
}

// Asiento es completo, o opaco con sólo Secuencia, HashAnterior y Hash.
type Asiento struct {
	Secuencia    int64   `json:"secuencia"`
	Opaco        bool    `json:"opaco,omitempty"`
	ID           string  `json:"id,omitempty"`
	Tipo         string  `json:"tipo,omitempty"`
	Evidencia    *string `json:"evidencia,omitempty"`
	Actor        string  `json:"actor,omitempty"`
	Timestamp    string  `json:"timestamp,omitempty"`
	Detalle      string  `json:"detalle,omitempty"`
	HashAnterior string  `json:"hashAnterior"`
	Hash         string  `json:"hash"`
}

// Sellos es sellos.json.
type Sellos struct {
	Formato string  `json:"formato"`
	Sellos  []Sello `json:"sellos"`
}

type Sello struct {
	Caso       string `json:"caso"`
	Secuencia  int64  `json:"secuencia"`
	HashCabeza string `json:"hashCabeza"`
	SelladoEn  string `json:"selladoEn"`
	ClaveID    string `json:"claveId"`
	Firma      string `json:"firma"`
}

// MarshalJSON escribe el asiento como lo pide el formato: el opaco sólo con secuencia, opaco,
// hashAnterior y hash; el completo con todos sus campos, incluida "evidencia": null.
func (a Asiento) MarshalJSON() ([]byte, error) {
	if a.Opaco {
		return json.Marshal(struct {
			Secuencia    int64  `json:"secuencia"`
			Opaco        bool   `json:"opaco"`
			HashAnterior string `json:"hashAnterior"`
			Hash         string `json:"hash"`
		}{a.Secuencia, true, a.HashAnterior, a.Hash})
	}
	return json.Marshal(struct {
		Secuencia    int64   `json:"secuencia"`
		ID           string  `json:"id"`
		Tipo         string  `json:"tipo"`
		Evidencia    *string `json:"evidencia"`
		Actor        string  `json:"actor"`
		Timestamp    string  `json:"timestamp"`
		Detalle      string  `json:"detalle"`
		HashAnterior string  `json:"hashAnterior"`
		Hash         string  `json:"hash"`
	}{a.Secuencia, a.ID, a.Tipo, a.Evidencia, a.Actor, a.Timestamp, a.Detalle, a.HashAnterior, a.Hash})
}
