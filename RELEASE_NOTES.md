## UhfTagAnalyzer 1.1.0 — první verze ověřená na reálné čtečce

Diagnostický a správcovský nástroj pro **UHF RFID tagy** (čtečka **CF601**, EPC Gen2 / ISO 18000‑6C).

Verze 1.0.0 vznikla před dodáním čtečky. Verze **1.1.0** je výsledkem testování na skutečné CF601
(FW V1.5.06.05) a opravuje chyby, kvůli kterým 1.0.0 s čtečkou prakticky nefungovala.
**Doporučujeme aktualizovat.**

### ⬇ Stažení a spuštění
Stáhni **`UhfTagAnalyzer-portable.zip`**, rozbal kamkoli a spusť **`UhfTagAnalyzer.exe`**.
Žádná instalace, žádné závislosti (není potřeba .NET ani Visual C++ runtime).

Balík obsahuje:
- `UhfTagAnalyzer.exe` — program (.NET runtime je uvnitř)
- `UHFPrimeReader.dll` + `hidapi.dll` — ovladač čtečky
- `UhfTagAnalyzer-manual.pdf` — český uživatelský manuál (21 stran, screenshoty ze skutečné čtečky)

> Všechny tři soubory programu musí zůstat **pohromadě** ve stejné složce.

### 🔧 Hlavní opravy
- **Čtečka „četla dokola" a program nic nenačetl** — CF601 je z výroby v režimu klávesnice. Program ji teď při připojení sám přepne do odpovědního režimu přes USB.
- **Čtení paměti vracelo data posunutá o jeden požadavek / „tag nenalezen"** — přepsán protokol čtení podle výrobce; čtení po inventuře cílí maskou na konkrétní tag.
- **Zápis, zámek a kill hlásily „OK", i když je tag odmítl** — výsledek se teď bere z odpovědi tagu.
- **Výběr tagu (select) nefungoval** — opravený ukazatel masky.
- **Identifikace čipu**: opravená tabulka výrobců a modelů (dříve např. M730/M750 prohozené); modely jen z datasheetů výrobců.
- **Region**: opravený číselník regionů; přepnutí nastavuje i frekvenční tabulku; při připojení mimo pásmo EU program nabídne přepnutí.
- **Test citlivosti** po skončení nevracel výkon (zůstal 1 dBm) — opraveno a ověřováno; test je 2,5× rychlejší.
- **Analýza bez tagu** trvala 35 s — teď skončí do ~5 s.
- Opravené dekódování XTID (sériové číslo čipu), EPC hlaviček GS1 a SSCC‑96; hesla s `0x` se už nezahazují.

### ✨ Novinky
- **Obnova zapomenutého hesla pouze čtením** — do tagu se nic nezapisuje; nalezené heslo se potvrdí.
- Report uvádí **sériové číslo čipu**, velikost paměti čipu a upozornění (sdílené heslo Impinj M700, Monza R6 bez hesel).
- Detekce **saturace RSSI** (tag přímo na anténě); RSSI se nepočítá do verdiktu.
- Soak test hodnotí podle úspěšnosti odezev (realisticky pro CF601).
- Verze v titulku okna; diagnostika bez GUI `--selftest-decode` / `--selftest`.

### 🗑 Odstraněno
- **Kanálový sken** — CF601 hlásí vždy kanál 0, test nic neměřil.
- **Tlačítko Reboot čtečky** — na CF601 vracelo region a režim do továrního stavu a nepomáhalo.

### ⚠️ Známá omezení
- Zápisové operace (zápis EPC/USER, heslo, lock, kill) jsou opravené, ale na hardwaru zatím neprošly plným testem — vyzkoušej je nejdřív na obětním tagu.
- Vzácné „zaseknutí" čtečky (nenajde tag, ač leží na čtečce) řeší odpojení a zapojení USB.

### ✅ Požadavky
- Windows 10 / 11 (32‑ i 64‑bit)
- USB čtečka CF601

---
*Hesse Valentino · hesse.works*
