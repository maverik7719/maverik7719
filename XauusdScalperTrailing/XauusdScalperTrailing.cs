using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    // Scalper trend-following per XAUUSD su M1.
    // Nessun Take Profit: si esce SOLO tramite Stop Loss.
    // Lo SL viene messo subito all'apertura, poi va in Break-Even e infine
    // segue il prezzo (trailing) restando a una distanza configurabile.
    // Quando il trend inverte il trailing SL viene colpito e la posizione chiude.
    public enum EntryStrategy
    {
        IncrocioEma,
        TrendPullback,
        Momentum
    }

    [Robot(AccessRights = AccessRights.None, TimeZone = TimeZones.UTC)]
    public class XauusdScalperTrailing : Robot
    {
        #region Parametri - Trade

        [Parameter("Lotti (se rischio % = 0)", Group = "Trade", DefaultValue = 0.01, MinValue = 0.01, Step = 0.01)]
        public double LotSize { get; set; }

        [Parameter("Etichetta (Label)", Group = "Trade", DefaultValue = "XAU_ScalperTrailing")]
        public string Label { get; set; }

        [Parameter("Una posizione alla volta", Group = "Trade", DefaultValue = true)]
        public bool SinglePosition { get; set; }

        [Parameter("Chiudi su segnale opposto", Group = "Trade", DefaultValue = true)]
        public bool CloseOnOppositeSignal { get; set; }

        [Parameter("Spread massimo (pip, 0 = off)", Group = "Trade", DefaultValue = 30, MinValue = 0)]
        public double MaxSpreadPips { get; set; }

        #endregion

        #region Parametri - Rischio

        // Sizing basato sul rischio: il volume viene calcolato in modo che, se
        // scatta lo Stop Loss iniziale, la perdita sia pari a questa % dell'equity.
        // Mettere a 0 per usare invece il volume fisso "Lotti".
        [Parameter("Rischio per trade (% equity, 0 = lotti fissi)", Group = "Rischio", DefaultValue = 0.5, MinValue = 0, Step = 0.1)]
        public double RiskPercentPerTrade { get; set; }

        // Stop di protezione: se l'equity scende di questa % rispetto al picco,
        // chiude tutto e smette di operare (evita di azzerare il conto). 0 = off.
        [Parameter("Halt: max drawdown % (0 = off)", Group = "Rischio", DefaultValue = 25, MinValue = 0)]
        public double MaxDrawdownPercent { get; set; }

        #endregion

        #region Parametri - Sessione

        // Filtro orario: opera solo nelle ore ad alta liquidità (UTC), evitando
        // la sessione asiatica dove lo scalping M1 tende a fare whipsaw.
        [Parameter("Filtro orario attivo", Group = "Sessione", DefaultValue = true)]
        public bool UseSessionFilter { get; set; }

        [Parameter("Ora inizio (UTC)", Group = "Sessione", DefaultValue = 7, MinValue = 0, MaxValue = 23)]
        public int SessionStartHour { get; set; }

        [Parameter("Ora fine (UTC)", Group = "Sessione", DefaultValue = 20, MinValue = 0, MaxValue = 23)]
        public int SessionEndHour { get; set; }

        #endregion

        #region Parametri - Volatilità (ATR)

        // Filtro di volatilità: opera solo quando c'è "movimento" sufficiente,
        // misurato dall'ATR. È un modo morbido per modulare la frequenza: invece
        // di spegnere interi orari/direzioni, salta solo le fasi piatte.
        [Parameter("Filtro ATR attivo", Group = "Volatilità (ATR)", DefaultValue = true)]
        public bool UseAtrFilter { get; set; }

        [Parameter("ATR periodo", Group = "Volatilità (ATR)", DefaultValue = 14, MinValue = 1)]
        public int AtrPeriod { get; set; }

        // Sotto questo ATR (in pip) il mercato è troppo fermo: niente trade.
        [Parameter("ATR minimo (pip)", Group = "Volatilità (ATR)", DefaultValue = 8, MinValue = 0)]
        public double MinAtrPips { get; set; }

        // Sopra questo ATR (in pip) la volatilità è eccessiva: niente trade. 0 = off.
        [Parameter("ATR massimo (pip, 0 = off)", Group = "Volatilità (ATR)", DefaultValue = 0, MinValue = 0)]
        public double MaxAtrPips { get; set; }

        #endregion

        #region Parametri - Stop Loss / Trailing

        [Parameter("Stop Loss iniziale (pip)", Group = "Stop / Trailing", DefaultValue = 50, MinValue = 1)]
        public double StopLossPips { get; set; }

        [Parameter("Attiva Break-Even a (pip di profitto)", Group = "Stop / Trailing", DefaultValue = 20, MinValue = 0)]
        public double BreakEvenTriggerPips { get; set; }

        [Parameter("Pip di sicurezza in Break-Even", Group = "Stop / Trailing", DefaultValue = 2, MinValue = 0)]
        public double BreakEvenExtraPips { get; set; }

        [Parameter("Attiva Trailing a (pip di profitto)", Group = "Stop / Trailing", DefaultValue = 25, MinValue = 0)]
        public double TrailingStartPips { get; set; }

        [Parameter("Distanza Trailing (pip)", Group = "Stop / Trailing", DefaultValue = 20, MinValue = 1)]
        public double TrailingDistancePips { get; set; }

        [Parameter("Passo minimo Trailing (pip)", Group = "Stop / Trailing", DefaultValue = 1, MinValue = 0)]
        public double TrailingStepPips { get; set; }

        #endregion

        #region Parametri - Ingresso (EMA)

        // Strategia di ingresso:
        // - IncrocioEma: entra sull'incrocio EMA veloce/lenta (trend-following classico).
        // - TrendPullback: in trend (EMA allineate) entra sui ritracciamenti quando il
        //   prezzo rientra oltre la EMA veloce. Meno trade, ingressi migliori per un
        //   sistema "no TP" che cavalca il trend.
        // - Momentum: entra dopo N candele consecutive chiuse nella stessa direzione
        //   (es. 2 candele rialziste => Buy). Reattivo, adatto a M1.
        [Parameter("Strategia ingresso", Group = "Ingresso", DefaultValue = EntryStrategy.TrendPullback)]
        public EntryStrategy Entry { get; set; }

        // Numero di candele consecutive nello stesso verso richieste dalla
        // strategia Momentum (una candela è "rialzista" se chiude sopra l'apertura).
        [Parameter("Momentum: candele consecutive", Group = "Ingresso", DefaultValue = 2, MinValue = 1)]
        public int MomentumCandles { get; set; }

        [Parameter("EMA veloce", Group = "Ingresso", DefaultValue = 9, MinValue = 1)]
        public int FastEmaPeriod { get; set; }

        [Parameter("EMA lenta", Group = "Ingresso", DefaultValue = 21, MinValue = 2)]
        public int SlowEmaPeriod { get; set; }

        [Parameter("Solo su incrocio (cross)", Group = "Ingresso", DefaultValue = true)]
        public bool TradeOnCrossOnly { get; set; }

        // Filtro di trend: compra solo se il prezzo è sopra questa EMA, vende solo
        // se è sotto. Riduce gli ingressi in controtrend (0 = filtro disattivato).
        [Parameter("Filtro trend EMA (periodo, 0 = off)", Group = "Ingresso", DefaultValue = 200, MinValue = 0)]
        public int TrendFilterPeriod { get; set; }

        // Barre minime di attesa tra un ingresso e il successivo: riduce
        // l'overtrading/whipsaw tipico dello scalping su M1.
        [Parameter("Barre minime tra i trade", Group = "Ingresso", DefaultValue = 3, MinValue = 0)]
        public int MinBarsBetweenTrades { get; set; }

        // Forza del segnale: al cross le EMA devono essere separate di almeno
        // questi pip. Scarta i cross "piatti" nel rumore (0 = disattivato).
        [Parameter("Distanza minima EMA al cross (pip, 0 = off)", Group = "Ingresso", DefaultValue = 5, MinValue = 0)]
        public double MinEmaGapPips { get; set; }

        #endregion

        private ExponentialMovingAverage _fastEma;
        private ExponentialMovingAverage _slowEma;
        private ExponentialMovingAverage _trendEma;
        private AverageTrueRange _atr;
        // Sentinella "nessun trade ancora fatto": valore basso ma sicuro, così
        // (Bars.Count - _lastTradeBarIndex) non va mai in overflow.
        private int _lastTradeBarIndex = -1000000;
        private double _peakEquity;
        private bool _tradingHalted;

        protected override void OnStart()
        {
            _fastEma = Indicators.ExponentialMovingAverage(Bars.ClosePrices, FastEmaPeriod);
            _slowEma = Indicators.ExponentialMovingAverage(Bars.ClosePrices, SlowEmaPeriod);
            if (TrendFilterPeriod > 0)
                _trendEma = Indicators.ExponentialMovingAverage(Bars.ClosePrices, TrendFilterPeriod);
            _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Exponential);

            if (FastEmaPeriod >= SlowEmaPeriod)
                Print("Attenzione: EMA veloce >= EMA lenta. Controlla i parametri.");

            _peakEquity = Account.Equity;

            Print("XAUUSD Scalper Trailing avviato su {0} {1}. PipSize={2}", SymbolName, TimeFrame, Symbol.PipSize);
        }

        // La gestione dello SL (Break-Even + Trailing) gira ad ogni tick per
        // reagire subito al movimento del prezzo.
        protected override void OnTick()
        {
            ManageOpenPositions();
            CheckDrawdownGuard();
        }

        // Stop di protezione del capitale: chiude tutto e blocca l'operatività
        // se l'equity scende oltre la % di drawdown dal picco.
        private void CheckDrawdownGuard()
        {
            if (_tradingHalted || MaxDrawdownPercent <= 0)
                return;

            if (Account.Equity > _peakEquity)
                _peakEquity = Account.Equity;

            if (_peakEquity <= 0)
                return;

            double drawdown = (_peakEquity - Account.Equity) / _peakEquity * 100.0;
            if (drawdown >= MaxDrawdownPercent)
            {
                _tradingHalted = true;
                foreach (var pos in GetMyPositions())
                    ClosePosition(pos);
                Print("STOP protezione: drawdown {0:F1}% >= {1}%. Operatività sospesa.", drawdown, MaxDrawdownPercent);
            }
        }

        // Le decisioni di ingresso vengono prese a barra chiusa (segnale stabile).
        protected override void OnBar()
        {
            var signal = GetSignal();
            if (signal.HasValue)
                HandleSignal(signal.Value);
        }

        #region Logica di ingresso

        // Ritorna Buy, Sell oppure null in base alla strategia scelta.
        private TradeType? GetSignal()
        {
            // Indice della barra appena chiusa.
            int i = Bars.Count - 2;
            if (i < 1)
                return null;

            TradeType? signal;
            switch (Entry)
            {
                case EntryStrategy.IncrocioEma:
                    signal = GetCrossSignal(i);
                    break;
                case EntryStrategy.Momentum:
                    signal = GetMomentumSignal(i);
                    break;
                default:
                    signal = GetPullbackSignal(i);
                    break;
            }

            if (signal.HasValue && !PassesTrendFilter(signal.Value, i))
                return null;

            return signal;
        }

        // Strategia 3: momentum di candele. Entra quando le ultime N candele chiuse
        // sono tutte nello stesso verso (corpo positivo => rialzista, e viceversa).
        private TradeType? GetMomentumSignal(int i)
        {
            int n = MomentumCandles;
            if (i - n + 1 < 0)
                return null;

            bool allBull = true;
            bool allBear = true;

            for (int k = 0; k < n; k++)
            {
                int idx = i - k;
                double open = Bars.OpenPrices[idx];
                double close = Bars.ClosePrices[idx];

                if (close <= open) allBull = false;
                if (close >= open) allBear = false;
            }

            if (allBull) return TradeType.Buy;
            if (allBear) return TradeType.Sell;
            return null;
        }

        // Strategia 1: incrocio EMA veloce/lenta.
        private TradeType? GetCrossSignal(int i)
        {
            double fastNow = _fastEma.Result[i];
            double slowNow = _slowEma.Result[i];
            double fastPrev = _fastEma.Result[i - 1];
            double slowPrev = _slowEma.Result[i - 1];

            bool crossedUp = fastPrev <= slowPrev && fastNow > slowNow;
            bool crossedDown = fastPrev >= slowPrev && fastNow < slowNow;

            // Filtro forza: scarta i cross con EMA troppo vicine (mercato piatto).
            if (MinEmaGapPips > 0 && Math.Abs(fastNow - slowNow) < MinEmaGapPips * Symbol.PipSize)
                return null;

            if (TradeOnCrossOnly)
            {
                if (crossedUp) return TradeType.Buy;
                if (crossedDown) return TradeType.Sell;
                return null;
            }

            // Modalità "trend continuo": segue la direzione delle EMA.
            if (fastNow > slowNow) return TradeType.Buy;
            if (fastNow < slowNow) return TradeType.Sell;
            return null;
        }

        // Strategia 2: trend + pullback. In trend (EMA veloce vs lenta allineate)
        // entra quando il prezzo, dopo un ritracciamento oltre la EMA veloce, la
        // riattraversa nella direzione del trend.
        private TradeType? GetPullbackSignal(int i)
        {
            double fastNow = _fastEma.Result[i];
            double slowNow = _slowEma.Result[i];
            double fastPrev = _fastEma.Result[i - 1];
            double closeNow = Bars.ClosePrices[i];
            double closePrev = Bars.ClosePrices[i - 1];

            bool trendUp = fastNow > slowNow;
            bool trendDown = fastNow < slowNow;

            // Long: in trend up il prezzo era sotto/uguale alla EMA veloce e ora
            // chiude sopra (rientro dopo il ritracciamento).
            if (trendUp && closePrev <= fastPrev && closeNow > fastNow)
                return TradeType.Buy;

            if (trendDown && closePrev >= fastPrev && closeNow < fastNow)
                return TradeType.Sell;

            return null;
        }

        // Filtro di trend: long solo sopra l'EMA lunga, short solo sotto.
        private bool PassesTrendFilter(TradeType signal, int barIndex)
        {
            if (_trendEma == null)
                return true;

            double price = Bars.ClosePrices[barIndex];
            double trend = _trendEma.Result[barIndex];

            if (signal == TradeType.Buy)
                return price > trend;
            return price < trend;
        }

        private void HandleSignal(TradeType signal)
        {
            if (_tradingHalted)
                return;

            var myPositions = GetMyPositions();

            // Gestione posizione opposta: la chiudo (ed eventualmente inverto).
            foreach (var pos in myPositions)
            {
                if (pos.TradeType != signal && CloseOnOppositeSignal)
                    ClosePosition(pos);
            }

            if (!IsWithinSession())
                return;

            if (!IsVolatilityOk())
                return;

            if (SinglePosition && HasPositionInDirection(signal))
                return;

            if (SinglePosition && GetMyPositions().Length > 0 && !CloseOnOppositeSignal)
                return;

            // Cooldown: evita di riaprire troppo presto (anti-whipsaw).
            if (MinBarsBetweenTrades > 0 && Bars.Count - _lastTradeBarIndex <= MinBarsBetweenTrades)
                return;

            if (!IsSpreadOk())
            {
                Print("Trade saltato: spread {0:F1} pip > limite {1} pip (parametro 'Spread massimo'). Alzalo o mettilo a 0 per disattivarlo.",
                    Symbol.Spread / Symbol.PipSize, MaxSpreadPips);
                return;
            }

            OpenTrade(signal);
        }

        private void OpenTrade(TradeType tradeType)
        {
            double volume = CalculateVolume();
            if (volume <= 0)
            {
                Print("Volume non valido/insufficiente per aprire il trade.");
                return;
            }

            // SL messo subito all'apertura. Nessun Take Profit (null).
            var result = ExecuteMarketOrder(tradeType, SymbolName, volume, Label, StopLossPips, null);
            if (result.IsSuccessful)
            {
                _lastTradeBarIndex = Bars.Count;
                Print("Aperta {0} vol {1} a {2} - SL iniziale {3} pip.", tradeType, volume, result.Position.EntryPrice, StopLossPips);
            }
            else
            {
                Print("Ordine fallito: {0}", result.Error);
            }
        }

        // Se "Rischio per trade" > 0, dimensiona il volume in base al rischio:
        // perdita a SL = RiskPercent% dell'equity. Altrimenti usa i lotti fissi.
        private double CalculateVolume()
        {
            if (RiskPercentPerTrade <= 0)
                return Symbol.NormalizeVolumeInUnits(Symbol.QuantityToVolumeInUnits(LotSize), RoundingMode.ToNearest);

            double riskAmount = Account.Equity * RiskPercentPerTrade / 100.0;
            double lossPerUnit = StopLossPips * Symbol.PipValue; // valore di 1 unità se colpisce lo SL
            if (lossPerUnit <= 0)
                return 0;

            double rawUnits = riskAmount / lossPerUnit;
            if (double.IsNaN(rawUnits) || double.IsInfinity(rawUnits) || rawUnits <= 0)
                return 0;

            // Non superare il volume massimo consentito dal simbolo.
            rawUnits = Math.Min(rawUnits, Symbol.VolumeInUnitsMax);
            double volume = Symbol.NormalizeVolumeInUnits(rawUnits, RoundingMode.Down);

            // Sotto il minimo: non forzo il volume minimo (rischierei più del previsto).
            if (volume < Symbol.VolumeInUnitsMin)
            {
                Print("Rischio {0}% troppo basso per il volume minimo: trade saltato.", RiskPercentPerTrade);
                return 0;
            }
            return volume;
        }

        #endregion

        #region Gestione SL: Break-Even + Trailing

        private void ManageOpenPositions()
        {
            foreach (var position in GetMyPositions())
            {
                double pipSize = Symbol.PipSize;
                double profitPips = position.Pips;

                if (position.TradeType == TradeType.Buy)
                {
                    // Prezzo a cui uscirei da un buy.
                    double referencePrice = Symbol.Bid;
                    double? newStop = null;

                    // 1) Break-Even
                    double bePrice = position.EntryPrice + BreakEvenExtraPips * pipSize;
                    if (BreakEvenTriggerPips > 0 && profitPips >= BreakEvenTriggerPips)
                        newStop = MaxNullable(newStop, bePrice);

                    // 2) Trailing
                    if (profitPips >= TrailingStartPips)
                    {
                        double trailPrice = referencePrice - TrailingDistancePips * pipSize;
                        newStop = MaxNullable(newStop, trailPrice);
                    }

                    ApplyStopForBuy(position, newStop, pipSize);
                }
                else
                {
                    // Prezzo a cui uscirei da un sell.
                    double referencePrice = Symbol.Ask;
                    double? newStop = null;

                    // 1) Break-Even
                    double bePrice = position.EntryPrice - BreakEvenExtraPips * pipSize;
                    if (BreakEvenTriggerPips > 0 && profitPips >= BreakEvenTriggerPips)
                        newStop = MinNullable(newStop, bePrice);

                    // 2) Trailing
                    if (profitPips >= TrailingStartPips)
                    {
                        double trailPrice = referencePrice + TrailingDistancePips * pipSize;
                        newStop = MinNullable(newStop, trailPrice);
                    }

                    ApplyStopForSell(position, newStop, pipSize);
                }
            }
        }

        // Per un BUY lo SL può solo salire (a favore) e di almeno TrailingStepPips.
        private void ApplyStopForBuy(Position position, double? candidate, double pipSize)
        {
            if (candidate == null)
                return;

            double step = TrailingStepPips * pipSize;
            double target = Math.Round(candidate.Value, Symbol.Digits);

            if (position.StopLoss.HasValue && target <= position.StopLoss.Value + step)
                return;

            // Non spostare lo SL sopra il prezzo corrente (verrebbe rifiutato).
            if (target >= Symbol.Bid)
                return;

            ModifyStop(position, target);
        }

        // Per un SELL lo SL può solo scendere (a favore) e di almeno TrailingStepPips.
        private void ApplyStopForSell(Position position, double? candidate, double pipSize)
        {
            if (candidate == null)
                return;

            double step = TrailingStepPips * pipSize;
            double target = Math.Round(candidate.Value, Symbol.Digits);

            if (position.StopLoss.HasValue && target >= position.StopLoss.Value - step)
                return;

            if (target <= Symbol.Ask)
                return;

            ModifyStop(position, target);
        }

        private void ModifyStop(Position position, double newStop)
        {
            var result = ModifyPosition(position, newStop, position.TakeProfit, ProtectionType.Absolute);
            if (result.IsSuccessful)
                Print("SL aggiornato per {0}: {1}", position.TradeType, newStop);
            else
                Print("Modifica SL fallita: {0}", result.Error);
        }

        #endregion

        #region Helper

        private Position[] GetMyPositions()
        {
            return Positions.FindAll(Label, SymbolName);
        }

        private bool HasPositionInDirection(TradeType tradeType)
        {
            foreach (var pos in GetMyPositions())
                if (pos.TradeType == tradeType)
                    return true;
            return false;
        }

        private bool IsSpreadOk()
        {
            if (MaxSpreadPips <= 0)
                return true;
            return Symbol.Spread / Symbol.PipSize <= MaxSpreadPips;
        }

        // Vero se la volatilità (ATR dell'ultima barra chiusa) è nell'intervallo
        // consentito: opera solo quando c'è movimento sufficiente.
        private bool IsVolatilityOk()
        {
            if (!UseAtrFilter)
                return true;

            int i = Bars.Count - 2;
            if (i < 0 || double.IsNaN(_atr.Result[i]))
                return true;

            double atrPips = _atr.Result[i] / Symbol.PipSize;

            if (MinAtrPips > 0 && atrPips < MinAtrPips)
                return false;
            if (MaxAtrPips > 0 && atrPips > MaxAtrPips)
                return false;
            return true;
        }

        // Vero se l'ora corrente (UTC) è dentro la finestra di sessione.
        private bool IsWithinSession()
        {
            if (!UseSessionFilter)
                return true;

            int hour = Server.Time.Hour;
            if (SessionStartHour == SessionEndHour)
                return true; // finestra "24h"
            if (SessionStartHour < SessionEndHour)
                return hour >= SessionStartHour && hour < SessionEndHour;
            // Finestra che scavalca la mezzanotte (es. 22 -> 6).
            return hour >= SessionStartHour || hour < SessionEndHour;
        }

        private static double? MaxNullable(double? current, double candidate)
        {
            if (current == null) return candidate;
            return Math.Max(current.Value, candidate);
        }

        private static double? MinNullable(double? current, double candidate)
        {
            if (current == null) return candidate;
            return Math.Min(current.Value, candidate);
        }

        #endregion
    }
}
