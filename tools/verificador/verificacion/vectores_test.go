package verificacion

import (
	"crypto/ed25519"
	"encoding/base64"
	"encoding/hex"
	"testing"
)

// Vectores de docs/formato-bundle-v1.md. Los fija también el servidor (RegistroDeCustodiaTests y
// FirmanteEd25519Tests): si esto falla, el verificador y el servidor no hablan el mismo formato.

const (
	hashDelVector  = "5949e6267194282c3eaaac6e9730ed1779ab2fd20d3b20d1bc02cbe04dec2b0a"
	firmaDelVector = "61908f5f1905f2742aa988653dc556bfe09e5ec62bb07311eef3b7646ac7b4256329bfae5c3069c0626f5e69260f132e1d53defb46774e3336831f06bc75580c"
	semillaRfc8032 = "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60"
	huellaRfc8032  = "ed25519:21fe31dfa154a261626bf854046fd2271b7bed4b6abe45aa58877ef47f9721b9"
)

func TestElHashDelAsientoDelVector(t *testing.T) {
	a := Asiento{
		Secuencia: 1, ID: "11111111-1111-1111-1111-111111111111", Tipo: "CasoAbierto", Actor: "ana",
		Timestamp: "2026-10-28T12:00:00.123456Z", Detalle: `{"numeroExpediente":"EXP-1"}`,
		HashAnterior: "0000000000000000000000000000000000000000000000000000000000000000",
	}

	if got := HashDeAsiento("22222222-2222-2222-2222-222222222222", a); got != hashDelVector {
		t.Fatalf("hash %s, el vector dice %s", got, hashDelVector)
	}
}

func TestLaFirmaDelSelloDelVector(t *testing.T) {
	semilla, _ := hex.DecodeString(semillaRfc8032)
	clave := ed25519.NewKeyFromSeed(semilla)
	s := Sello{
		Caso: "22222222-2222-2222-2222-222222222222", Secuencia: 1, HashCabeza: hashDelVector,
		SelladoEn: "2026-10-28T13:00:00.000000Z", ClaveID: huellaRfc8032,
	}

	if got := huellaDe(clave); got != huellaRfc8032 {
		t.Fatalf("huella %s, el vector dice %s", got, huellaRfc8032)
	}
	if got := hex.EncodeToString(ed25519.Sign(clave, MensajeDeSello(s))); got != firmaDelVector {
		t.Fatalf("firma %s, el vector dice %s", got, firmaDelVector)
	}
	firma, _ := hex.DecodeString(firmaDelVector)
	s.Firma = base64.StdEncoding.EncodeToString(firma)
	if !FirmaDeSelloValida(s, clave.Public().(ed25519.PublicKey)) {
		t.Fatal("la firma del vector no verifica")
	}
}
