# Plán testů – UhfTagAnalyzer + čtečka CF601

Stav k **2026-11-11**. Čtečka: CF601, FW `UHF Desk Reader V1.5.06.05`, HW `CP-206600_Y_V1.1 / modul UCM601-v221`,
SN `220700773138350D544A3434`. Referenční tag: **Impinj M750** (EPC 96 bit, USER 32 bit, sdílené heslo), EPC `300ED89F3350016A1E641753`,
TID `E280119020006A5A2F720353`, RESERVED zamčená neznámým heslem.

## Jak testujeme

| Nástroj | Co dělá | Čtečka | Tag |
|---|---|---|---|
| `UhfTagAnalyzer.exe --selftest-decode` | offline testy dekodérů (CRC, TID/XTID, EPC, hesla, regiony) → `selftest-decode.log`, exit kód = počet chyb | ne | ne |
| `UhfTagAnalyzer.exe --selftest` | připojení + analýza + dump + test výběru tagu, **jen čtení** → `selftest.log` | ano | volitelně |
| `--selftest --set-region 6` | navíc přepne region (6 = EU/ETSI 4 kanály) | ano | volitelně |
| `--selftest --sens` | navíc test citlivosti (mění výkon, na konci vrátí) | ano | ano |
| GUI | interaktivní testy (záložky) | ano | dle testu |

Role: **Claude** spouští headless testy a vyhodnocuje logy; **uživatel** manipuluje s tagem/USB a kliká v GUI.

Značky: 🚫 bez tagu · 🏷️ tag potřeba · 🔌 odpojit/zapojit USB · ✍️ zapisuje do tagu · ⚠️ nevratné

Pravidla:
- „Bez tagu" = tag **dál než ~1 m** od čtečky (dosah je až 35 cm – tag ležící na stole vedle čtečky se čte!).
- ✍️ testy **jen na obětním tagu**, nikdy na referenčním M750.
- ⚠️ KILL a PERMALOCK **jen na výslovný pokyn**.

---

## Fáze 0 – offline (bez čtečky)

| ID | Test | Očekávání | Stav |
|---|---|---|---|
| T0.1 | `--selftest-decode` | 33/33 OK | ✅ 2026-11-11 |

## Fáze A – čtečka, bez tagu 🚫

| ID | Test | Postup | Očekávání |
|---|---|---|---|
| A1 | Připojení po studeném startu 🔌 | odpojit USB, 5 s, zapojit → `--selftest` | log „Režim čtečky: USB + answer", připojení < 10 s |
| A2 | **Trvalost nastavení** 🔌 | po A1 zkontrolovat region / WORKMODE / PORT / výkon | rozhodne, zda `SetDevicePara` ukládá trvale (očekávám EU, 0, USB, 17 dBm) |
| A3 | Analýza bez tagu | GUI Analyzovat (nebo selftest) | „Žádný tag", **< 5 s** (dříve 35 s – opraveno) |
| A4 | Sken bez tagu | GUI Sken 10 s, Stop | UI nezamrzá, prázdný seznam |
| A5 | Konfigurace – načíst | GUI Konfigurace → Načti | region EU 865,7–867,5 MHz, port USB, **teplota a protokol** dávají smysl (neověřené funkce `GetTemperature`, `GetRFIDType`) |
| A6 | ~~Reboot čtečky~~ | — | **zrušeno v 1.1.0**: tlačítko Reboot odstraněno (RebootDevice vracel region USA + WORKMODE 1). Místo toho A7. |
| A7 | Region mimo EU při připojení | (jen když čtečka hlásí jiný region) Připojit | dotaz „Přepnout na EU / ETSI?" → Ano → region 6, stavový řádek „EU / ETSI – 4 kanaly" |

## Fáze B – tag, jen čtení 🏷️

| ID | Test | Postup | Očekávání |
|---|---|---|---|
| B1 | Analýza – tag ~10 cm | GUI Analyzovat | FUNKČNÍ 4/4, Impinj M750, CRC OK, RSSI realistické (cca −35 až −60 dBm) |
| B2 | Analýza – tag přímo na čtečce | totéž | FUNKČNÍ; RSSI může být −90 (saturace přijímače) – jen se zaznamená |
| B3 | Dump paměti | GUI Paměť → Dump | EPC 8 slov, TID 9 slov (sériové číslo 6A5A2F720353), USER 2 slova, RESERVED „chybné heslo" |
| B4 | Sken + CSV (2 tagy, máš-li) | GUI Sken 10 s → Export CSV | oba tagy v seznamu, CSV se otevře v Excelu |
| B5 | Výběr tagu | `--selftest` (sekce VYBER) | správné EPC projde, chybné EPC −249 |
| B6 | **Test citlivosti** | tag ~10 cm, GUI Testy, Pmin 0 / Pmax 17 | jde shora dolů, končí po 2 krocích bez čtení, ~20–40 s; výkon po testu **ověřeně** zpět na 17 dBm ✅ |
| B7 | Analýza hned po B6 | GUI Analyzovat | FUNKČNÍ bez odpojení USB ✅ (zaseknutí po sérii SetRFPower se NEpotvrdilo) |
| B8 | Soak 30 s | GUI Testy → Soak | známka A (úspěšnost ≥ 95 %), ~20 čtení/s, žádné zamrznutí |
| B9 | ~~Kanálový sken~~ | — | **zrušeno v 1.1.0**: CF601 hlásí vždy kanál 0 – funkce odstraněna |
| B10 | Obnova hesla – slovník (zamčený M750) | GUI Operace → Spustit obnovu | žádný falešný nález, ~4 kandidáti/s (✅ ověřeno selftestem) |
| B11 | Odpojení / zavření během operace | spustit Soak 60 s → zavřít okno | aplikace nespadne, zavře se po doběhnutí (log „Zavírám po dokončení…") |

## Fáze C – zápis ✍️ (obětní tag)

Předpoklad: obětní tag, nejlépe bez hesla (RESERVED čitelná, heslo 00000000). Po každém kroku kontrola Analýzou/Dumpem.

| ID | Test | Očekávání |
|---|---|---|
| C1 | Zápis USER – stejná hodnota | „Zápis OK", obsah beze změny |
| C2 | Zápis USER – nová hodnota, pak vrátit | dump ukáže novou hodnotu; vrácení OK |
| C3 | Zápis EPC (nové), pak vrátit původní | nové EPC v analýze, **PC zachová UMI/AFI** (mění se jen délka), CRC OK, vybraný tag v aplikaci se přepne na nové EPC |
| C4 | Zápis s chybným nenulovým heslem | **chyba** (ne falešné „OK") – ověří nové vyhodnocování odpovědí tagu |
| C5 | Nastavit access heslo (např. `11223344`) | RESERVED pak bez hesla „chybné heslo", s heslem čitelná |
| C6 | **Obnova hesla – pozitivní případ**: maska `112233XX` | najde `11223344` (≤ ~1 min) a potvrdí přečtením RESERVED |
| C7 | Lock EPC heslem (akce 2) | zápis EPC bez hesla selže, s heslem projde |
| C8 | Unlock EPC (akce 0) s heslem | zápis bez hesla opět projde |
| C9 | Vrátit access heslo na `00000000` | tag zpět ve výchozím stavu |
| C10 ⚠️ | KILL (jen na pokyn) | tag zmizí z inventury |
| C11 ⚠️ | PERMALOCK | **netestovat** bez výslovného pokynu |

## Fáze D – distribuce

| ID | Test | Očekávání |
|---|---|---|
| D1 | Portable ZIP na jiném PC bez .NET | spustí se, najde čtečku, přepne ji do USB + answer |
| D2 | `--selftest-decode` z portable složky | 23/23 OK |

---

## Známé problémy a otevřené otázky

- **Trvalost nastavení**: `SetDevicePara` je jediný způsob uložení (výrobce nic jiného nevolá); PORT=USB a WORKMODE=0
  přežily odpojení napájení. `RebootDevice` vrátil REGION=1 (USA) a WORKMODE=1 → tlačítko Reboot v 1.1.0 odstraněno,
  aplikace při připojení vždy opraví port/režim a nabídne region EU. Region po odpojení napájení → test A2 (při akceptaci 1.1.0).
- **Zaseknutí inventury** (`InventoryContinue` −238 až do odpojení USB): pozorováno 2× na začátku ladění. Série změn
  výkonu ho NEvyvolává (B6/B7 ✅). Pozdější „tag nenalezen" byla jiná chyba (výkon nevrácený na 1 dBm, opraveno).
  Řešení, kdyby se objevilo: odpojit/zapojit USB (uvedeno v manuálu).
- **Session S2 (tovární)**: čtení hned po inventuře BEZ masky selhává (0/10, kódy −249/−250), S maskou podle EPC 10/10 →
  analýza vždy cílí maskou. Vysvětluje i dřívější „záhadné" −250.
- **RSSI** při plném výkonu s tagem na čtečce saturuje (−90 dBm) – report to detekuje; do verdiktu se nepočítá.
- **GetRFPower** vrací na CF601 něco jiného než výkon v dBm (vrátil 1) → nepoužívat; výkon číst z DevicePara.RFIDPOWER.
- ~~Tabulka výrobců a modelů~~ **vyřešeno 2026-11-11**: MDID dle registru GS1, modely (TMN) jen z datasheetů
  výrobců (Impinj Monza 4 / R6 / R6-P / M700, NXP UCODE 7/8/9/9xm/9xe, Alien Higgs-3/4). Referenční tag je
  **M750** (dříve chybně M730 – tabulka měla M730/M750 prohozené). Čipy mimo tabulku se hlásí jako „model TMN … – není v tabulce".
- **XTID**: dekódujeme jen serializaci (ověřeno i datasheety Impinj/NXP: hlavička 0x2000 = 48bit serial); příznaky segmentů vypisujeme jen jako syrovou hodnotu.
- **Obnova hesla**: rychlost ~4 kandidáti/s je limit čtečky (Access se špatným heslem trvá ~225 ms). Maska `XXXX` ≈ 4–5 h.
- **Zápisové operace** (write/lock/kill/heslo) jsou po opravě neověřené na hardwaru → fáze C.

## Záznam výsledků

| ID | Datum | Výsledek | Poznámka |
|---|---|---|---|
| T0.1 | 2026-11-11 | ✅ 23/23 | našel chybu parseru `0x` prefixu (opraveno) |
| T0.1 | 2026-11-11 | ✅ 33/33 | po přepisu tabulek MDID/TMN dle datasheetů (+10 testů) |
| B1 | 2026-11-11 | ✅ | selftest, tag poblíž: FUNKČNÍ 4/4, RSSI −37 dBm |
| B3 | 2026-11-11 | ✅ | selftest dump 1,8 s |
| B5 | 2026-11-11 | ✅ | selftest |
| B10 | 2026-11-11 | ✅ | selftest, 19 kandidátů, bez falešného nálezu |
| B6 | 2026-11-11 | ✅ | min. výkon 5 dBm (tag ~10 cm), 41 s, výkon ověřeně vrácen na 17 dBm |
| B7 | 2026-11-11 | ✅ | analýza hned po B6: FUNKČNÍ 4/4 |
| B8 | 2026-11-11 | ✅ | soak 5 s: 100 % odezev, 19,8 čtení/s, známka A |
| A5 | 2026-11-11 | ✅ | EU 865,7–867,5, port USB, teplota 36–44 °C / práh 80 °C, protokol Gen2 |

## Akceptační test verze 1.1.0 (uživatel)

Projdi v GUI, výsledek napiš (OK / co nesedí):

1. 🔌 Odpoj čtečku z USB, zapoj, spusť `UhfTagAnalyzer.exe` → titulek **v1.1.0** → **Připojit**.
   Stavový řádek: `UHF Desk Reader V1.5.06.05 · EU / ETSI – 4 kanaly · 17 dBm` (= test A1 + A2 trvalost).
   Kdyby se objevil dotaz na region → napiš to (znamená, že se region po vypnutí neuloží) a odpověz Ano.
2. 🚫 Tag dál než 1 m → **Analyzuj tag** → „Žádný tag nenalezen" do ~5 s (A3).
3. 🏷️ Tag na čtečku → **Analyzuj tag** → FUNKČNÍ, Impinj M750, sériové číslo čipu, poznámka o sdíleném hesle (B1/B2).
4. **Paměť → Dump** (B3), **Sken** 10 s → **Export CSV** (B4).
5. **Testy → Test citlivosti** (B6), pak hned **Analyzuj tag** (B7), **Soak test** (B8).
6. **Konfigurace → Načti konfiguraci** (A5).
7. Spusť Soak 60 s a **zavři okno** během testu → program počká a zavře se bez pádu (B11).
