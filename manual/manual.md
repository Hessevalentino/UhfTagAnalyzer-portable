## O tomto manuálu

Tento dokument je kompletní uživatelská i technická příručka k programu **UhfTagAnalyzer** –
diagnostickému a správcovskému nástroji pro UHF RFID tagy čtené stolní čtečkou **CF601**.

Manuál je psaný tak, abys po jeho přečtení **rozuměl celému systému** – nejen které tlačítko
zmáčknout, ale i *co se děje uvnitř tagu a čtečky* a *proč* se věci chovají, jak se chovají.
Je rozdělený na dvě roviny:

- **Prakticky** – jak program ovládat, co dělá každá záložka, krok za krokem.
- **Do hloubky** – jak funguje EPC Gen2, paměťové banky, hesla, zámky, identifikace čipu.

Pokud chceš jen rychle začít, přečti si kapitoly *Instalace a spuštění* a *Připojení ke čtečce*
a pak záložku *Analýza*. Zbytek je referenční – vrátíš se k němu, až ho budeš potřebovat.

{: .box }

---

## 1. Úvod

### 1.1 Co je UhfTagAnalyzer

UhfTagAnalyzer je program pro **Windows**, který přes čtečku CF601 pracuje s pasivními
**UHF RFID tagy** (štítky) standardu **EPC Gen2 / ISO 18000-6C**. Umožňuje:

- **přečíst tag a zjistit o něm maximum** – jeho EPC kód, výrobce a model čipu, obsah paměti,
  stav hesel a zámků,
- **posoudit, zda je tag funkční** a jak kvalitní má signál,
- **zapisovat** do tagu (EPC, uživatelská data), **zamykat** paměť, nastavovat **hesla**,
  v krajním případě tag **nevratně zničit** (kill),
- **obnovit zapomenuté přístupové heslo** vlastního tagu,
- **testovat čtečku a tagy** (citlivost, frekvenční odezva, stabilita čtení, teplota),
- **hromadně skenovat** více tagů a exportovat je do CSV.

### 1.2 Pro koho je určen

Pro každého, kdo pracuje s UHF RFID štítky – ve skladu, výrobě, laboratoři, při vývoji nebo
servisu. Nepředpokládá hluboké znalosti RFID; vše potřebné vysvětluje kapitola 2.

### 1.3 Co je to vlastně „tag"

RFID tag (štítek, transpondér) je malý čip s anténou. **Pasivní** UHF tag nemá baterii –
energii i povel dostává rádiem ze čtečky. Čtečka vyšle pole, tag se „probudí", napájí se z něj
a odpovídá zpět. Proto má dosah jen několik centimetrů až metrů a proto na kvalitě signálu
tolik záleží.

---

## 2. Jak funguje UHF RFID (teorie, kterou chceš znát)

Tato kapitola je srdcem pochopení celého programu. Bez ní jsou pojmy jako *PC slovo*, *TID*
nebo *lock* jen tlačítka; s ní budeš vědět, co děláš.

### 2.1 Standard EPC Gen2 / ISO 18000-6C

UHF tagy, se kterými CF601 pracuje, dodržují standard **EPC Gen2** (shodný s ISO 18000-6C).
Pracují v pásmu **860–960 MHz** (v Evropě 865–868 MHz). Standard určuje, jak tag komunikuje,
jak je uspořádaná jeho paměť a jaké příkazy umí (čtení, zápis, zámek, kill).

### 2.2 Paměť tagu – čtyři banky

Paměť každého EPC Gen2 tagu je rozdělená do čtyř **bank**. Toto je nejdůležitější tabulka
celého manuálu – vrátíš se k ní u záložek Paměť a Operace:

| Banka | Číslo | Co obsahuje |
|---|---|---|
| **RESERVED** | 0 | **Kill heslo** (slova 0–1) a **Access heslo** (slova 2–3). Každé 32 bitů. |
| **EPC** | 1 | **CRC** (slovo 0), **PC slovo** (slovo 1) a vlastní **EPC kód** (od slova 2). |
| **TID** | 2 | **Tag ID** – z výroby napevno. Určuje výrobce a model čipu, často unikátní sériové číslo. |
| **USER** | 3 | **Uživatelská paměť** – volná pro tvoje data. Velikost se liší podle čipu (0 až stovky bitů). |

Paměť se adresuje po **slovech** (1 slovo = 16 bitů = 2 bajty). Když tedy čteš „od slova 2,
2 slova", čteš bajty na pozicích 4–7.

### 2.3 EPC kód

**EPC** (Electronic Product Code) je „číslo tagu" – to, co se při inventuře čte jako první.
Může to být náhodné ID, nebo strukturovaný kód podle GS1 (např. **SGTIN‑96** nese globální
číslo produktu GTIN + sériové číslo). Typicky má 96 bitů (12 bajtů), ale délka je proměnná.

### 2.4 PC slovo (Protocol Control)

Před EPC je v bance 1 jedno **PC slovo**. Je to „hlavička", která tagu i čtečce říká:

- **délku EPC** (horních 5 bitů) – kolik slov EPC má,
- **UMI** (User Memory Indicator) – jestli tag má data v uživatelské paměti,
- **XPC** – zda je přítomno rozšířené PC,
- **toggle** – jestli je EPC formátu GS1/EPC, nebo ISO.

Program PC slovo automaticky rozkládá a ukazuje ti, co znamená. Když měníš EPC, PC slovo se
musí přepočítat podle nové délky – to program dělá za tebe.

### 2.5 CRC – kontrola integrity

**CRC‑16** je kontrolní součet uložený v bance EPC. Program ho umí **přepočítat** z PC+EPC a
porovnat s tím, co je v tagu. Shoda = data v tagu nejsou poškozená. Neshoda může znamenat
poškozený tag nebo chybu čtení.

### 2.6 TID – rodný list čipu

**TID** (banka 2) je z výroby **napevno a nezměnitelně** zapsané ID čipu. Program z něj pozná:

- **třídu** – `E2` znamená EPC Gen2 / ISO 18000‑6C,
- **výrobce** (pole MDID) – Impinj, NXP, Alien, EM, STMicro…,
- **model čipu** (pole TMN) – např. Impinj Monza R6, NXP UCODE 8/9, Alien Higgs‑3,
- zda je TID **serializované** – tj. unikátní pro každý kus.

Protože TID nejde změnit a čte se vždy, je to spolehlivý ukazatel, **co přesně máš za tag**.
Když se TID nedá přečíst, je to varovný signál – obvykle slabý signál nebo vadný čip.

### 2.7 Hesla a zámky

Tag má dvě 32bitová hesla (v bance RESERVED):

- **Access heslo** – „klíč" k zamčeným bankám. Se správným access heslem lze zapisovat do
  oblastí, které jsou jinak uzamčené.
- **Kill heslo** – slouží k **nevratnému zničení** tagu (příkaz kill).

**Zámky (lock)** určují, co jde s danou bankou dělat. Pro každou oblast lze nastavit:

| Akce | Význam |
|---|---|
| **odemčeno** | zápis volně, bez hesla |
| **trvale odemčeno** | navždy zapisovatelné, nelze už zamknout |
| **zamčeno heslem** | zápis jen se správným access heslem |
| **PERMALOCK** | **trvalý zámek – nevratné, neobejde se ani heslem** |

Toto je klíč k pochopení záložky Operace i obnovy hesla. **Permalock je definitivní** – co
permalocknete, už nikdy nezměníte.

---

## 3. Čtečka CF601

| Parametr | Hodnota |
|---|---|
| Typ | stolní UHF RFID čtečka / zapisovačka |
| Připojení | **USB** (hlásí se jako **HID** zařízení) |
| Protokol | EPC Gen2 / ISO 18000‑6C |
| Pásmo | 860–960 MHz (EU 865–868 MHz) |
| Vysílací výkon | cca 17 dBm |
| Čtecí dosah | přibližně **1–35 cm** (čtení nablízko) |
| Napájení | z USB (5 V) |

Čtečka se k PC připojuje přes USB a program s ní komunikuje přes nativní knihovnu
`UHFPrimeReader.dll` (+ `hidapi.dll`). Žádné ovladače se obvykle neinstalují – Windows HID
zařízení rozpozná samo.

> **Pracovní režim (WORKMODE).** Čtečka umí pracovat ve více režimech. Pro čtení a zápis
> paměti musí být v **odpovědním režimu** (`WORKMODE = 0`). Pokud program po připojení hlásí
> `WORKMODE != 0`, přepni ho v záložce **Konfigurace** – jinak nebudou fungovat operace s pamětí.

{: .box }

---

## 4. Instalace a spuštění

### 4.1 Co potřebuješ

Program je **přenosný** – nic se neinstaluje. Potřebuješ jen jeho složku se **třemi soubory**:

```
UhfTagAnalyzer.exe      – samotný program (.NET runtime je uvnitř)
UHFPrimeReader.dll      – ovladač čtečky
hidapi.dll              – závislost pro USB
```

- Běží na **Windows 10/11** (32‑ i 64‑bitových). Program je 32bitový, protože ovladač čtečky je
  32bitový – na 64bit Windows poběží bez problému.
- **Nepotřebuje** nainstalovaný .NET ani Visual C++ redistributable. Vše potřebné je uvnitř.

### 4.2 Spuštění

1. Zkopíruj celou složku (nebo rozbal `UhfTagAnalyzer-portable.zip`) kamkoli na disk.
2. Spusť **`UhfTagAnalyzer.exe`** dvojklikem.

> **Důležité:** všechny tři soubory musí zůstat **pohromadě ve stejné složce**. Kdyby exe
> zůstal bez `UHFPrimeReader.dll`, program při připojení ohlásí „Dll was not found".

{: .box }

---

## 5. Přehled rozhraní

![Hlavní okno – záložka Analýza](img/01-analyza.png)

Okno má pevnou strukturu, která je stejná ve všech záložkách:

- **Hlavička** (nahoře) – banner s názvem programu a podpisem autora.
- **Panel připojení** – kontrolka (LED), tlačítko **Připojit** (zelené) a **Odpojit** (červené).
- **Záložky** – Analýza, Sken / CSV, Paměť, Testy, Konfigurace, Operace.
- **Pracovní plocha** – obsah aktivní záložky.
- **Log** (dole) – průběžný záznam toho, co program dělá, s časem.
- **Stavový řádek** (úplně dole) – stav připojení, vybraný tag, parametry čtečky a podpis.

**LED kontrolka** vedle tlačítka Připojit je šedá, když je čtečka odpojená, a **zelená**, když
je připojená. Rychlý vizuální indikátor stavu.

---

## 6. Připojení ke čtečce

1. Zapoj CF601 do USB.
2. Klikni na **Připojit**.
3. Program najde čtečku (přes USB‑HID) a do stavového řádku vypíše její **firmware, hardware,
   sériové číslo, region a výkon**. LED zezelená, stav se změní na „Připojeno".

Když čtečka není nalezena, program zobrazí hlášku „CF601 nenalezena na USB" – zkontroluj kabel
a že se čtečka v systému hlásí jako HID zařízení.

Po skončení práce klikni **Odpojit**. Při zavření okna se čtečka odpojí automaticky.

> **Tip:** většina akcí v ostatních záložkách vyžaduje připojenou čtečku. Pokud klikneš na
> akci bez připojení, program to napíše do logu a nic neprovede.

{: .box }

---

## 7. Záložka Analýza

Nejdůležitější záložka. Přiložíš tag a klikneš **Analyzuj tag** – program provede kompletní
rozbor jednoho tagu a vypíše přehledný report. Tím se tag zároveň „vybere" pro ostatní záložky.

![Záložka Analýza](img/01-analyza.png)

- **Inventura (s)** – jak dlouho program tag „poslouchá" (výchozí 1,0 s). Delší = spolehlivější
  měření kvality signálu.
- **Analyzuj tag** – spustí rozbor.
- **Uložit report** – uloží výsledek jako textový soubor (.txt).
- Vlevo je **seznam detekovaných tagů**, vpravo **report**.

### 7.1 Jak číst report

Report má šest sekcí:

**[1] Detekce** – kolik tagů je v poli, **úspěšnost čtení v %** (kolikrát tag odpověděl z počtu
pokusů → kvalita signálu), **RSSI** (síla signálu v dBm), anténa a kanál. Vyšší úspěšnost a
vyšší (méně záporné) RSSI = lepší.

**[2] PC slovo** – délka EPC, **UMI** (má tag user paměť?), schéma GS1/ISO a **kontrola
EPC CRC‑16** (OK = data konzistentní). Tady je i výklad obsahu EPC (např. rozpad SGTIN‑96 na
GTIN a sériové číslo).

**[3] TID – identifikace čipu** – surové TID, třída, **výrobce a model čipu**, zda je
serializované. Toto odpovídá na otázku „co přesně je to za tag".

**[4] RESERVED – hesla** – stav kill a access hesla: *default* (samé nuly), *nastaveno*, nebo
nelze přečíst (banka je čtecsky zamčená = tag je zabezpečený).

**[5] USER paměť** – velikost a obsah uživatelské paměti (hex + ASCII náhled).

**[6] Verdikt** – pět kontrol (detekce, stabilita čtení, čitelnost TID, CRC, síla signálu) a
barevný závěr:

| Verdikt | Význam |
|---|---|
| **FUNKČNÍ** (zeleně) | tag čte spolehlivě, TID i CRC v pořádku |
| **ČÁSTEČNĚ FUNKČNÍ** (oranžově) | tag reaguje, ale slabě / nestabilně |
| **NEFUNKČNÍ** (červeně) | tag se nepodařilo spolehlivě přečíst – vadný nebo mimo dosah |

> Když je v poli víc tagů, program analyzuje ten s nejsilnějším signálem a ostatní vypíše do
> seznamu. Pro čistý rozbor měj na čtečce ideálně jen jeden tag.

{: .box }

---

## 8. Záložka Sken / CSV

Slouží k **hromadnému, průběžnému skenování** více tagů a jejich evidenci.

![Záložka Sken / CSV](img/02-sken.png)

- **Start skenu** – spustí průběžné čtení. Do seznamu přibývají **unikátní tagy** s počtem
  čtení, RSSI, anténou, kanálem a časem posledního záznamu.
- **Stop** – zastaví sken.
- **Vyčistit** – smaže seznam.
- **Export CSV** – uloží nasbírané tagy do CSV souboru (oddělovač `;`) pro Excel nebo evidenci.

Hodí se na inventuru, ověření čtivosti většího počtu štítků nebo sběr dat o tazích.

---

## 9. Záložka Paměť

Kompletní **výpis (dump) všech čtyř bank** tagu – pro hlubší pohled, než dává záložka Analýza.

![Záložka Paměť](img/03-pamet.png)

- **Access heslo (hex)** – pokud je některá banka čtecsky chráněná, zadej sem heslo.
- **Dump všech bank** – přečte RESERVED, EPC, TID i USER a vypíše je jako **tabulku slov**
  (po 16bitových slovech), ASCII náhled, odvození zámků a výklad EPC/TID.

Když některou banku nelze přečíst, program napíše proč – banka je zamčená, tag ji nemá, nebo
je slabý signál. I to je informace.

---

## 10. Záložka Testy

Diagnostika rádiové části – pomáhá ladit umístění tagu, antény a posoudit kvalitu.

![Záložka Testy](img/04-testy.png)

### 10.1 Test citlivosti

Rampuje **vysílací výkon** od `Pmin` do `Pmax` a u každé hodnoty změří, jestli (a jak dobře)
se tag přečte. Výsledkem je **minimální výkon, při kterém se tag poprvé přečte** – přímá míra
citlivosti tagu a kvality antény. Čím nižší, tím lepší.

### 10.2 Kanálový sken

Delší inventura s rozpadem **po frekvenčních kanálech** – ukáže počet čtení a průměrné RSSI na
každém kanálu. Odhalí, na kterých frekvencích tag čte nejlépe, případně rušení.

### 10.3 Soak test

Dlouhé měření stability (délku zadáš v „Soak s"). Spočítá **čtení za sekundu**, rozptyl RSSI a
přidělí **známku stability A–F**. Výborné pro ověření, že tag čte spolehlivě v čase, ne jen
jednou.

### 10.4 Teplota čtečky

Přečte aktuální teplotu čtečky a teplotní práh – pro kontrolu, že se čtečka nepřehřívá.

---

## 11. Záložka Konfigurace

Čtení a nastavení parametrů čtečky.

![Záložka Konfigurace](img/05-konfigurace.png)

- **Načti konfiguraci** – zobrazí snapshot: firmware, hardware, SN, region, výkon, Q, session,
  anténa, protokol, teplota, frekvence.
- **Ulož do čtečky** – zapíše nastavené parametry.
- **Reboot čtečky** – restartuje čtečku (po potvrzení).

Nastavitelné parametry:

| Parametr | Co dělá |
|---|---|
| **Region** | frekvenční pásmo dle země. V ČR/EU zvol **EU 865–868**. |
| **Výkon** | vysílací výkon v dBm (vyšší = větší dosah, u CF601 cca do 17). |
| **Q** | parametr antikolize – ovlivňuje čtení více tagů najednou. |
| **Session** | relace EPC Gen2 (0–3); ovlivňuje chování při opakovaném čtení. |
| **WorkMode** | pracovní režim. **Pro čtení/zápis paměti nastav 0** (odpovědní režim). |

> **Nastavení regionu je důležité.** Když je region špatně (např. US místo EU), čtečka nemusí
> v Evropě správně číst. Po změně klikni *Ulož do čtečky*.

{: .box }

---

## 12. Záložka Operace

Zde se tag **mění**. Všechny akce nejdřív pracují s tagem vybraným v záložce Analýza a používají
**Access heslo** z horní sekce. Nebezpečné akce jsou odděleny a vyžadují potvrzení.

![Záložka Operace](img/06-operace.png)

### 12.1 Přístup

Políčko **Access heslo (hex)** – 4 bajty (8 hex znaků), např. `00000000`. Použije se pro
všechny operace níže (zápis, zámek, nastavení hesla).

### 12.2 Zápis dat

- **Zapsat EPC** – přepíše EPC tagu. Zadáš nový EPC v hex (po celých slovech, tj. sudý počet
  bajtů). Program **automaticky přepočítá PC slovo** podle nové délky.
- **Zapsat USER** – zapíše data do uživatelské paměti od zvoleného slova.

### 12.3 Hesla a zámky

- **Nastavit heslo** – zapíše nové **access** nebo **kill** heslo (4 bajty hex) do banky
  RESERVED.
- **Zamknout** – nastaví zámek vybrané oblasti (Kill heslo / Access heslo / EPC / TID / USER)
  na zvolenou akci (odemčeno / trvale odemčeno / zamčeno heslem / **PERMALOCK**).

> **Pozor na PERMALOCK.** Je **nevratný**. Permalocknutou oblast už nikdy nezměníš, ani se
> správným heslem. Program před permalockem vyžaduje potvrzení – ber ho vážně.

{: .box }

### 12.4 Kill tag

**KILL TAG** tag **nevratně zničí** – po killu už nikdy neodpoví na žádný příkaz. Používá KILL
heslo (zadané v poli „nové (hex)" v sekci Hesla, jinak Access heslo). Kill s nulovým heslem
není povolen. Program vyžaduje potvrzení.

> Zničený tag se **nedá obnovit**. Kill používej jen vědomě a na správném tagu.

{: .box }

---

## 13. Obnova zapomenutého access hesla

Sekce **Obnova zapomenutého access hesla** (v záložce Operace) pomáhá, když jsi zapomněl
přístupové heslo **vlastního** tagu.

### 13.1 Jak to funguje (princip orákula)

Tag ti nikdy neřekne, jestli je heslo správné. Program proto používá **orákulum** – operaci,
která uspěje jen se správným heslem:

1. Vybere tvůj tag a přečte jeho aktuální PC+EPC.
2. Ověří, že je **EPC banka zamčená** (jinak by prošlo i špatné heslo → obnova se odmítne).
3. Pro každé kandidátní heslo zkusí **zapsat zpět ta samá data** do EPC banky:
   - špatné heslo → čtečka vrátí chybu „zamčeno/špatné heslo" → zkouší další,
   - správné heslo → zápis projde → **heslo nalezeno**.

Protože se zapisují **identická data**, na tagu se nic nezmění – metoda je **nedestruktivní**.

### 13.2 Odkud se berou kandidáti

- **Chytrý slovník** – běžná default hesla a vzory (`00000000`, `FFFFFFFF`, `12345678`…) a
  hesla **odvozená z EPC a TID** tagu.
- **Vlastní kandidáti** – napíšeš je do pole, oddělené čárkami, každé 8 hex znaků.
- **Nahraný slovník** – tlačítkem *Nahrát slovník ze souboru* načteš textový soubor s hesly
  (po řádcích nebo oddělené čárkami/mezerami, každé 8 hex znaků).
- **Generátor podle masky** – pole *Maska* + *Generovat a zkusit*. Napíšeš 8 znaků: pevné hex
  číslice + `X` tam, kde heslo neznáš. Program vyzkouší všechny kombinace.

### 13.3 Kolik je kombinací a jak dlouho to trvá

Access heslo má **32 bitů = 2³² ≈ 4,3 miliardy** kombinací. Generovat je je snadné, ale brzdí
**rychlost čtečky** – každý pokus je rádiový příkaz (desítky ms). Proto má smysl prostor
**zmenšit maskou**:

| Maska | Kombinací | Odhad (~20 pokusů/s) |
|---|---|---|
| `1234XXXX` | 65 536 | ~55 minut |
| `123456XX` | 256 | pár sekund |
| `XXXXXXXX` (celé) | 4,3 miliardy | ~7 let (nereálné) |

Při běhu program ukazuje **rychlost a odhad zbývajícího času**; tlačítko **Zastavit** obnovu
kdykoli přeruší.

### 13.4 Meze obnovy

- Funguje **jen na zamčeném tagu** (jinak orákulum nedává smysl).
- **Permalock** se neobejde ani správným heslem – nelze obnovit.
- Plný brute‑force celého prostoru je prakticky neproveditelný; obnova má smysl jen se
  slovníkem nebo když znáš část hesla (maska).

> Obnova je určena pro **tvoje vlastní tagy**, kde jsi zapomněl heslo. Je to provozní nástroj,
> ne prostředek k obcházení cizího zabezpečení.

{: .box }

---

## 14. Bezpečnost a nevratné operace

Shrnutí všeho, co **nelze vzít zpět** – čti, než klikneš:

- **KILL TAG** – tag je navždy zničený, neodpoví už na nic.
- **PERMALOCK** – permalocknutá banka je navždy neměnná, ani s heslem.
- **Přepsání EPC/USER** – přepíše původní obsah; pokud sis ho nezapsal, je pryč.
- **Nastavení nového hesla** – když ho zapomeneš a banku zamkneš, dostaneš se k ní jen obnovou
  (a jen pokud je heslo uhodnutelné).

Doporučení: nové postupy si vyzkoušej nejdřív na „obětním" tagu, ne na ostrých datech. Před
zápisem/zámkem si udělej **dump paměti** (záložka Paměť), ať máš původní stav zaznamenaný.

---

## 15. Řešení problémů

| Problém | Příčina a řešení |
|---|---|
| **„Dll was not found"** při Připojit | Chybí `UHFPrimeReader.dll` nebo `hidapi.dll` vedle exe. Ujisti se, že jsou **všechny 3 soubory pohromadě**. |
| **„CF601 nenalezena na USB"** | Čtečka není zapojená nebo ji systém nevidí jako HID. Zkontroluj kabel, zkus jiný USB port. |
| **Čtení paměti selhává** | Čtečka není v odpovědním režimu. V **Konfiguraci** nastav `WORKMODE = 0`. |
| **TID/paměť nelze přečíst** | Slabý signál (přilož tag blíž, dosah 1–35 cm) nebo je banka zamčená. |
| **Zápis/zámek vrací chybu hesla** | Oblast je zamčená a access heslo nesedí. Zadej správné, nebo zkus obnovu. |
| **Tag se vůbec nedetekuje** | Špatný region (nastav EU), nízký výkon, nebo vadný/mrtvý tag. |
| **Obnova hesla „nefunguje – banka není zamčená"** | Orákulum funguje jen na zamčeném tagu. Pokud není zamčený, heslo není potřeba. |

---

## 16. Slovníček pojmů

**EPC** – Electronic Product Code; „číslo tagu" v bance EPC.
**Gen2 / ISO 18000‑6C** – standard UHF RFID, který CF601 používá.
**Banka** – jedna ze čtyř oblastí paměti tagu (RESERVED, EPC, TID, USER).
**Slovo** – 16 bitů (2 bajty); jednotka adresace paměti.
**PC slovo** – Protocol Control; hlavička před EPC (délka, UMI, schéma).
**CRC** – kontrolní součet pro ověření integrity EPC.
**TID** – Tag ID; neměnné ID čipu (výrobce, model, sériové číslo).
**MDID / TMN** – pole v TID: identifikátor výrobce / číslo modelu čipu.
**UMI** – User Memory Indicator; příznak, že tag má data v USER paměti.
**RSSI** – síla přijatého signálu v dBm (méně záporné = silnější).
**Access heslo** – klíč k zamčeným bankám.
**Kill heslo** – heslo pro nevratné zničení tagu.
**Lock / Permalock** – zámek banky / trvalý nevratný zámek.
**WORKMODE** – pracovní režim čtečky; 0 = odpovědní (nutný pro čtení/zápis paměti).
**Inventura** – rychlé čtení EPC všech tagů v poli.

---

## 17. Technické přílohy

### 17.1 Paměťové banky – číselné kódy

| Banka | memBank |
|---|---|
| RESERVED | 0 |
| EPC | 1 |
| TID | 2 |
| USER | 3 |

### 17.2 LockTag – oblasti a akce

**Oblast (erea):** 0 = Kill heslo, 1 = Access heslo, 2 = EPC, 3 = TID, 4 = USER.

**Akce (action):**

| Hodnota | Pro hesla (Kill/Access) | Pro data (EPC/TID/USER) |
|---|---|---|
| 0 | čtení/zápis volně | zápis volně |
| 1 | vždy čtení/zápis | vždy zapisovatelné |
| 2 | čtení/zápis jen s heslem | zápis jen s heslem |
| 3 | nelze číst/zapsat (skryté) | **PERMALOCK** (nevratné) |

### 17.3 Rozklad TID

- bajt 0 = `E2` → třída EPC Gen2 / ISO 18000‑6C,
- následuje 12bitové pole za `E2`: nejvyšší bit = XTID indikátor, nižší bity = **MDID**
  (identifikátor výrobce),
- dalších 12 bitů = **TMN** (číslo modelu čipu),
- delší serializované TID nese navíc unikátní sériové číslo kusu.

### 17.4 Identifikace čipu – příklady

| Surové TID (začátek) | Čip |
|---|---|
| `E2 80 11 70` | Impinj Monza R6 |
| `E2 80 69 94` | NXP UCODE 9 |
| `E2 80 69 1x` | NXP UCODE 8 |
| `E2 00 34 12` | Alien Higgs‑3 |

### 17.5 Technické poznámky k programu

- Program je **.NET WinForms**, 32bitový (x86), protože nativní `UHFPrimeReader.dll` je x86.
- Komunikace se čtečkou probíhá přes **USB‑HID** (funkce `OpenHidConnection`).
- Čtení/zápis paměti využívá dvojici příkazů `ReadTag` → `GetReadTagResp`, resp. `WriteTag`.
- Výrobce čipu (MDID) je dle EPC Tag Data Standard spolehlivý; **model (TMN) je heuristika**
  podle tabulky běžných čipů – proto program vždy ukazuje i surové TID k ověření.

---

*UhfTagAnalyzer — Hesse Valentino · hesse.works*
