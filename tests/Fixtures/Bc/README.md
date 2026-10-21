# BC-Fixtures (WireMock)

Antworten des Business-Central-Ersatzes als Dateien. Die Struktur folgt dem WireMock-Standard:
`mappings/*.json` (Anfrage → Antwort) und `__files/*` (Antwortkörper).

Der AppHost bindet diesen Ordner nach `/home/wiremock` des Containers `bc-mock`.
Die Mappings entstehen in Phase 3, sobald das BC-Datenmodell geklärt ist (`TODO(BC-Modell)`).
