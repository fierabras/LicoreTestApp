# LicoreTestApp — Herramienta de prueba de integración licore.dll

## Requisitos
- Windows x64
- .NET 8 Runtime
- licore.dll (Release x64) en el mismo directorio que el ejecutable

## Uso rápido

1. Colocar `licore.dll` junto al ejecutable.
2. (Opcional) En la sección **Validate License**, expandir *Variables de entorno de test*:
   - **LICORE_TEST_FINGERPRINT**: fingerprint esperado por la licencia de prueba
   - **LICORE_TEST_TODAY**: fecha simulada `YYYY-MM-DD` para pruebas de expiración
3. Usar los paneles en orden: Ping → Reason → Validate → Generate → Install.
4. **Run All** ejecuta la secuencia completa automáticamente.

## Paneles

| Panel | Función |
|---|---|
| Ping / Version | `lc_ping()` + `lc_version()` — verifica que la DLL responde |
| Reason Message | `lc_reason_message(code)` — consulta descripción de un código |
| Validate License | `lc_validate_full` y `lc_validate_cached` con producto/versión configurables |
| Generate Request | `lc_generate_request` → JSON visible; "Guardar .req" lo persiste vía `lc_write_request_file` |
| Install License | `lc_install_license` + post-install validate automático |
| Ejecución completa | Ejecuta los 8 pasos en secuencia; muestra `X/N OK` al finalizar |

## Logs

- **Log de sesión**: visible en el ListView derecho; copiable con *Copiar Log*.
- **Log automático (Run All)**: `%TEMP%\licore_testapp_YYYYMMDD_HHmmss.log`
- **Crash log**: `%TEMP%\licore_testapp_crash.log` (append por sesión)

## Colores de fila

- Verde (`#E8F5E9`): `ApiResult == Ok`
- Rojo (`#FFEBEE`): cualquier otro resultado

## Datos sensibles

- No se registran fingerprints completos, firmas ni PII en ningún log.
- Los campos Tax ID y Email solo se usan en la generación del `.req` y no se persisten entre sesiones.
