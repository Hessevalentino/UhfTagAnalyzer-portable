## UhfTagAnalyzer — přenosná verze (portable)

Diagnostický a správcovský nástroj pro **UHF RFID tagy** (čtečka **CF601**, EPC Gen2 / ISO 18000‑6C).

### ⬇ Stažení a spuštění
Stáhni **`UhfTagAnalyzer-portable.zip`**, rozbal kamkoli a spusť **`UhfTagAnalyzer.exe`**.
Žádná instalace, žádné závislosti (není potřeba .NET ani Visual C++ runtime).

Balík obsahuje:
- `UhfTagAnalyzer.exe` — program (.NET runtime je uvnitř)
- `UHFPrimeReader.dll` + `hidapi.dll` — ovladač čtečky
- `UhfTagAnalyzer-manual.pdf` — český uživatelský manuál (19 stran)

### ✅ Požadavky
- Windows 10 / 11 (32‑ i 64‑bit)
- USB čtečka CF601

> Všechny tři soubory musí zůstat **pohromadě** ve stejné složce.

### ✨ Co umí
Analýza tagu (identifikace čipu z TID, kontrola CRC, výklad EPC) · hromadný sken + CSV ·
dump paměti · testy (citlivost, kanály, soak, teplota) · konfigurace čtečky ·
operace (zápis EPC/USER, lock/kill, nastavení hesel) · obnova zapomenutého přístupového hesla.

---
*Hesse Valentino · hesse.works*
