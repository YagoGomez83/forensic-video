package main

import "syscall"

// prepararConsola pone la consola de Windows en UTF-8, para que los acentos se lean bien también
// en cmd.exe sin configurar nada.
func prepararConsola() {
	const utf8 = 65001
	syscall.NewLazyDLL("kernel32.dll").NewProc("SetConsoleOutputCP").Call(utf8) //nolint:errcheck
}
