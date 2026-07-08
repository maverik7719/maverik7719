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
- **Lotti** — dimensione posizione (es. 0.01).
- **Etichetta (Label)** — identifica le posizioni del bot.
- **Una posizione alla volta** — evita di sovrapporre più trade.
- **Chiudi su segnale opposto** — quando il trend inverte chiude subito (oltre allo SL).
- **Spread massimo (pip)** — non entra se lo spread è troppo alto (0 = disattivato).

### Stop / Trailing
- **Stop Loss iniziale (pip)** — SL messo all'apertura.
- **Attiva Break-Even a (pip di profitto)** — quando spostare lo SL al pareggio.
- **Pip di sicurezza in Break-Even** — quanti pip di margine bloccare al pareggio.
- **Attiva Trailing a (pip di profitto)** — da quando comincia a seguire il prezzo.
- **Distanza Trailing (pip)** — quanto sta "sotto/sopra" il prezzo.
- **Passo minimo Trailing (pip)** — sposta lo SL solo se migliora di almeno tot pip.

### Ingresso (EMA)
- **EMA veloce** / **EMA lenta** — periodi delle medie.
- **Solo su incrocio (cross)** — entra solo sull'incrocio; se disattivato segue il trend continuo.

## Note importanti

- I valori dei "pip" seguono la definizione del broker per l'oro (`Symbol.PipSize`). Su molti broker
  1 pip = 0.1 sul prezzo dell'oro: **verifica sempre** in demo prima di usarlo con soldi reali.
- I parametri di default sono un **punto di partenza**, non ottimizzati. Fai backtest/ottimizzazione
  su cTrader e testa in **conto demo** prima del reale.
- Questo software è fornito "così com'è", senza garanzie. Il trading comporta rischio di perdita del capitale.
