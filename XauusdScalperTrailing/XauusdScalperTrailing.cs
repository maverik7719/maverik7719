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
    [Robot(AccessRights = AccessRights.None, TimeZone = TimeZones.UTC)]
    public class XauusdScalperTrailing : Robot
    {
        #region Parametri - Trade

        [Parameter("Lotti", Group = "Trade", DefaultValue = 0.01, MinValue = 0.01, Step = 0.01)]
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

        [Parameter("EMA veloce", Group = "Ingresso", DefaultValue = 9, MinValue = 1)]
        public int FastEmaPeriod { get; set; }

        [Parameter("EMA lenta", Group = "Ingresso", DefaultValue = 21, MinValue = 2)]
        public int SlowEmaPeriod { get; set; }

        [Parameter("Solo su incrocio (cross)", Group = "Ingresso", DefaultValue = true)]
        public bool TradeOnCrossOnly { get; set; }

        #endregion

        private ExponentialMovingAverage _fastEma;
        private ExponentialMovingAverage _slowEma;

        protected override void OnStart()
        {
            _fastEma = Indicators.ExponentialMovingAverage(Bars.ClosePrices, FastEmaPeriod);
            _slowEma = Indicators.ExponentialMovingAverage(Bars.ClosePrices, SlowEmaPeriod);

            if (FastEmaPeriod >= SlowEmaPeriod)
                Print("Attenzione: EMA veloce >= EMA lenta. Controlla i parametri.");

            Print("XAUUSD Scalper Trailing avviato su {0} {1}. PipSize={2}", SymbolName, TimeFrame, Symbol.PipSize);
        }

        // La gestione dello SL (Break-Even + Trailing) gira ad ogni tick per
        // reagire subito al movimento del prezzo.
        protected override void OnTick()
        {
            ManageOpenPositions();
        }

        // Le decisioni di ingresso vengono prese a barra chiusa (segnale stabile).
        protected override void OnBar()
        {
            var signal = GetSignal();
            if (signal.HasValue)
                HandleSignal(signal.Value);
        }

        #region Logica di ingresso

        // Ritorna Buy, Sell oppure null in base al trend delle EMA.
        private TradeType? GetSignal()
        {
            // Indice della barra appena chiusa.
            int i = Bars.Count - 2;
            if (i < 1)
                return null;

            double fastNow = _fastEma.Result[i];
            double slowNow = _slowEma.Result[i];
            double fastPrev = _fastEma.Result[i - 1];
            double slowPrev = _slowEma.Result[i - 1];

            bool crossedUp = fastPrev <= slowPrev && fastNow > slowNow;
            bool crossedDown = fastPrev >= slowPrev && fastNow < slowNow;

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

        private void HandleSignal(TradeType signal)
        {
            var myPositions = GetMyPositions();

            // Gestione posizione opposta: la chiudo (ed eventualmente inverto).
            foreach (var pos in myPositions)
            {
                if (pos.TradeType != signal && CloseOnOppositeSignal)
                    ClosePosition(pos);
            }

            if (SinglePosition && HasPositionInDirection(signal))
                return;

            if (SinglePosition && GetMyPositions().Length > 0 && !CloseOnOppositeSignal)
                return;

            if (!IsSpreadOk())
            {
                Print("Trade saltato: spread troppo alto ({0:F1} pip).", Symbol.Spread / Symbol.PipSize);
                return;
            }

            OpenTrade(signal);
        }

        private void OpenTrade(TradeType tradeType)
        {
            double volume = Symbol.NormalizeVolumeInUnits(Symbol.QuantityToVolumeInUnits(LotSize), RoundingMode.ToNearest);
            if (volume <= 0)
            {
                Print("Volume non valido per {0} lotti.", LotSize);
                return;
            }

            // SL messo subito all'apertura. Nessun Take Profit (null).
            var result = ExecuteMarketOrder(tradeType, SymbolName, volume, Label, StopLossPips, null);
            if (result.IsSuccessful)
                Print("Aperta {0} {1} a {2} - SL iniziale {3} pip.", tradeType, volume, result.Position.EntryPrice, StopLossPips);
            else
                Print("Ordine fallito: {0}", result.Error);
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
