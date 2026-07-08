# XAUUSD Scalper Trailing (cTrader cBot)

cBot per **cTrader** che fa **scalping trend-following su XAUUSD (M1)** in BUY e SELL.

Idea di base:

- **Nessun Take Profit**: si esce **solo** con lo Stop Loss.
- Lo **SL viene messo subito** all'apertura, a una distanza configurabile in pip.
- Appena la posizione va in guadagno lo SL viene portato in **Break-Even** (pareggio, o +qualche pip di sicurezza).
- Poi parte il **trailing**: lo SL "segue" il prezzo restando a una distanza configurabile e si muove **solo a favore**.
- Quando il **trend inverte**, il trailing SL viene colpito e la posizione si chiude.

L'ingresso di default usa l'**incrocio di due EMA** (veloce/lenta). È volutamente semplice: è il punto che possiamo cambiare/affinare insieme.

## File

- `XauusdScalperTrailing/XauusdScalperTrailing.cs` — codice del cBot.
- `XauusdScalperTrailing/XauusdScalperTrailing.csproj` — progetto per cTrader Automate.

## Installazione

### Metodo semplice (consigliato)

1. Apri **cTrader** → sezione **Automate**.
2. Crea un **New cBot**.
3. Incolla il contenuto di `XauusdScalperTrailing.cs` sostituendo tutto.
4. Premi **Build**.
5. Vai su un grafico **XAUUSD / M1**, aggiungi l'istanza del cBot, imposta i parametri e **Play**.

### Metodo progetto (.csproj)

Il `.csproj` usa il pacchetto NuGet `cTrader.Automate` e produce direttamente un file `.algo`.
Aprilo/compilalo con l'editor di cTrader (o `dotnet build`) e poi installa il `.algo` generato.

## Parametri

### Trade
- **Lotti (se rischio % = 0)** — volume fisso usato solo quando il rischio % è 0.
- **Etichetta (Label)** — identifica le posizioni del bot.
- **Una posizione alla volta** — evita di sovrapporre più trade.
- **Chiudi su segnale opposto** — quando il trend inverte chiude subito (oltre allo SL).
- **Spread massimo (pip)** — non entra se lo spread è troppo alto (0 = disattivato).

### Rischio
- **Rischio per trade (% equity, 0 = lotti fissi)** — dimensiona il volume in modo che,
  se scatta lo SL iniziale, la perdita sia pari a questa % dell'equity. È la protezione
  principale contro l'azzeramento del conto. Metti 0 per usare i "Lotti" fissi.
- **Halt: max drawdown % (0 = off)** — se l'equity scende di questa % dal picco, chiude
  tutto e sospende l'operatività (impedisce di arrivare a 0).

### Sessione
- **Filtro orario attivo** — opera solo in una finestra oraria (UTC).
- **Ora inizio / Ora fine (UTC)** — finestra ad alta liquidità (default 7–20), per
  evitare la sessione asiatica dove lo scalping M1 fa più whipsaw.

### Volatilità (ATR)
Modo "morbido" per modulare la frequenza: opera solo quando c'è movimento reale,
invece di spegnere interi orari/direzioni.
- **Filtro ATR attivo** — attiva/disattiva il filtro.
- **ATR periodo** — periodo dell'ATR (default 14).
- **ATR minimo (pip)** — sotto questa volatilità (mercato piatto) non entra.
- **ATR massimo (pip, 0 = off)** — sopra questa volatilità (troppo caos) non entra.

Suggerimento: con il filtro ATR attivo puoi **allentare** i filtri secchi
(distanza minima EMA a 0, filtro orario off, filtro trend più basso) e regolare la
frequenza soprattutto con **ATR minimo**: più alto = meno trade ma più "mossi".

### Stop / Trailing
- **Stop Loss iniziale (pip)** — SL messo all'apertura.
- **Attiva Break-Even a (pip di profitto)** — quando spostare lo SL al pareggio.
- **Pip di sicurezza in Break-Even** — quanti pip di margine bloccare al pareggio.
- **Attiva Trailing a (pip di profitto)** — da quando comincia a seguire il prezzo.
- **Distanza Trailing (pip)** — quanto sta "sotto/sopra" il prezzo.
- **Passo minimo Trailing (pip)** — sposta lo SL solo se migliora di almeno tot pip.

### Ingresso (EMA)
- **Strategia ingresso** — `IncrocioEma` (entra sull'incrocio EMA) oppure `TrendPullback`
  (in trend, entra sui ritracciamenti quando il prezzo riattraversa la EMA veloce nella
  direzione del trend). `TrendPullback` genera meno trade e più mirati.
- **EMA veloce** / **EMA lenta** — periodi delle medie.
- **Solo su incrocio (cross)** — (solo modalità IncrocioEma) entra solo sull'incrocio;
  se disattivato segue il trend continuo.
- **Filtro trend EMA (periodo, 0 = off)** — long solo se il prezzo è sopra questa EMA lunga,
  short solo se è sotto. Riduce gli ingressi in controtrend (default 200).
- **Barre minime tra i trade** — cooldown anti-whipsaw tra un ingresso e il successivo.
- **Distanza minima EMA al cross (pip, 0 = off)** — richiede che al cross le EMA siano
  separate di almeno N pip, così scarta i cross "piatti" nel rumore.

## Perché un test può andare a -100% (e come evitarlo)

Con volume fisso e senza controllo del rischio, una serie di stop può azzerare il conto,
soprattutto su M1 dove l'incrocio EMA genera molti falsi segnali (overtrading/whipsaw).
Per questo ora, di default:

- il volume è calcolato sul **rischio %** (una perdita = solo la % scelta dell'equity);
- c'è un **filtro di trend** (EMA 200) che taglia gli ingressi controtrend;
- c'è un **cooldown** tra i trade.

Consiglio per il backtest: parti con **Rischio 0.5%**, **Filtro trend 200**, **Barre minime 3**,
e valuta anche di alzare le EMA (es. 21/50) per ridurre il rumore su M1.

## Note importanti

- I valori dei "pip" seguono la definizione del broker per l'oro (`Symbol.PipSize`). Su molti broker
  1 pip = 0.1 sul prezzo dell'oro: **verifica sempre** in demo prima di usarlo con soldi reali.
- I parametri di default sono un **punto di partenza**, non ottimizzati. Fai backtest/ottimizzazione
  su cTrader e testa in **conto demo** prima del reale.
- Questo software è fornito "così com'è", senza garanzie. Il trading comporta rischio di perdita del capitale.
