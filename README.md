# XAUUSD M1 Scalping Bot — cTrader

Bot di scalping per **XAUUSD** su timeframe **M1**, sviluppato per cTrader/cAlgo.

## Logica principale

1. **Nessun Take Profit** — la posizione si chiude solo quando viene colpito lo Stop Loss.
2. **SL iniziale** — appena apre BUY/SELL, mette subito lo SL a X pips (configurabile).
3. **Break Even** — quando il trade guadagna Y pips, sposta lo SL a entry (+ piccolo offset opzionale).
4. **Trailing Stop** — mentre il trend continua, lo SL segue il prezzo sempre Z pips indietro.
5. **Chiusura** — quando il prezzo inverte e tocca lo SL trailing, la posizione si chiude.

```
Apertura → SL fisso sotto/sopra
    ↓ profitto >= soglia BE
Break Even (SL a entry)
    ↓ trend continua
Trailing SL (sempre N pips dietro al prezzo)
    ↓ inversione
Hit SL → chiusura
```

## Strategie di entry (da scegliere insieme)

| Strategia | Descrizione |
|-----------|-------------|
| **EmaCrossover** (default) | BUY quando EMA veloce incrocia sopra EMA lenta; SELL al contrario |
| **RsiReversal** | BUY su rimbalzo da ipervenduto; SELL su rimbalzo da ipercomprato |
| **CandleMomentum** | BUY se N candele bullish consecutive; SELL se bearish |

La strategia si cambia dal parametro **"Strategia entry"** nell'interfaccia del cBot.

## Parametri consigliati per XAUUSD M1

| Parametro | Valore suggerito | Note |
|-----------|------------------|------|
| SL iniziale | 25–40 pips | Oro è volatile; adatta al broker |
| Profitto per BE | 8–15 pips | Quando spostare a break-even |
| Offset BE | 0–2 pips | 0 = BE esatto; 1–2 = piccolo buffer |
| Trailing SL | 10–20 pips | Distanza dal prezzo durante il trail |
| Spread max | 30–40 pips | Filtro per evitare entry con spread alto |
| Volume | 0.01 | Adatta al tuo capitale |

## Installazione in cTrader

1. Apri **cTrader** → **Algo** → **New cBot**
2. Crea un nuovo robot chiamato `XauUsdM1ScalpingBot`
3. Copia il contenuto di `Sources/Robots/XauUsdM1ScalpingBot/XauUsdM1ScalpingBot.cs`
4. Compila (Build)
5. Aggancia il bot su **XAUUSD** con timeframe **M1**
6. Configura i parametri e avvia su **demo** prima del live

## Prossimi passi (da definire insieme)

- Quale strategia di entry preferisci (EMA, RSI, momentum, o combinazione)?
- Orari di trading (sessione Londra/NY)?
- Filtro trend su timeframe superiore (es. solo BUY se H1 è bullish)?
- Gestione del volume / risk % sul capitale?

## Disclaimer

Questo software è a scopo educativo. Il trading comporta rischi significativi. Testa sempre su conto demo prima di usare capitale reale.
