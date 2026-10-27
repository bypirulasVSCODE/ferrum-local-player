# FERRUM

> **Beta en desarrollo** — FERRUM todavía no está terminado. El código está abierto para que la comunidad lo pruebe, reporte errores y contribuya mejoras.

Reproductor ligero de música local para Windows con paleta metal rojo y negro, biblioteca persistente, playlists, favoritos, perfiles y controles de teclado.

## Funciones

- Reproducción de archivos locales, con audio cargado al reproducir para reducir RAM.
- Playlists con portada, descripción, controles aleatorios y opción para compartir metadatos.
- Perfiles locales, favoritos, carátulas y persistencia en el dispositivo.
- Eliminación de canciones de FERRUM sin borrar el archivo original; al borrar una playlist se conservan las canciones.

## Descargar y usar

Descarga el paquete listo para Windows: [FERRUM-Beta-Windows.zip](FERRUM-Beta-Windows.zip). Descomprímelo y ejecuta `FERRUM-Beta.exe` o `Iniciar-FERRUM.cmd`. Importa archivos o carpetas de audio. Los archivos se guardan en IndexedDB local; playlists y perfiles no se sincronizan entre equipos.

## Estado y limitaciones

Esta es una beta inicial. Puede haber errores, diferencias en formatos compatibles según el navegador y cambios en el formato de datos. Haz copias de seguridad de tus archivos originales. Los perfiles solo existen en ese equipo y navegador. FERRUM no incluye ni distribuye música.

## Contribuir

¡Puedes meterle mano! Abre un issue para reportar errores o proponer ideas; para cambios, crea un fork y envía un pull request. Consulta [CONTRIBUTING.md](CONTRIBUTING.md). Mantén los cambios enfocados, explica cómo verificaste la modificación y no subas música, tokens ni datos personales.

## Teclas

- Espacio: reproducir o pausar.
- Teclas multimedia: anterior/siguiente.
- Ctrl+K: buscar en la biblioteca.

## Compilar en Windows

Con .NET Framework 4.x:

```powershell
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $compiler /nologo /target:winexe /optimize+ /out:FERRUM-Beta.exe /win32icon:assets\FERRUM.ico /resource:ferrum-player.html,FerrumPlayer.html /reference:System.Windows.Forms.dll /reference:System.Drawing.dll windows\Program.cs
```

Licencia MIT.


