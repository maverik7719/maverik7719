using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class XauUsdM1ScalpingBot : Robot
    {
        private const string BotLabel = "XauM1Scalp";

        // --- Gestione rischio / SL ---
        [Parameter("SL iniziale (pips)", Group = "Stop Loss", DefaultValue = 30, MinValue = 1)]
        public double InitialStopLossPips { get; set; }

        [Parameter("Profitto per BE (pips)", Group = "Stop Loss", DefaultValue = 10, MinValue = 0)]
        public double BreakEvenTriggerPips { get; set; }

        [Parameter("Offset BE (pips)", Group = "Stop Loss", DefaultValue = 1, MinValue = 0)]
        public double BreakEvenOffsetPips { get; set; }

        [Parameter("Trailing SL (pips)", Group = "Stop Loss", DefaultValue = 15, MinValue = 1)]
        public double TrailingStopPips { get; set; }

        // --- Entry / strategia ---
        [Parameter("Strategia entry", Group = "Strategia", DefaultValue = EntryStrategy.EmaCrossover)]
        public EntryStrategy Strategy { get; set; }

        [Parameter("EMA veloce", Group = "Strategia", DefaultValue = 9, MinValue = 2)]
        public int FastEmaPeriod { get; set; }

        [Parameter("EMA lenta", Group = "Strategia", DefaultValue = 21, MinValue = 3)]
        public int SlowEmaPeriod { get; set; }

        [Parameter("RSI periodo", Group = "Strategia", DefaultValue = 14, MinValue = 2)]
        public int RsiPeriod { get; set; }

        [Parameter("RSI ipervenduto", Group = "Strategia", DefaultValue = 30, MinValue = 1, MaxValue = 49)]
        public double RsiOversold { get; set; }

        [Parameter("RSI ipercomprato", Group = "Strategia", DefaultValue = 70, MinValue = 51, MaxValue = 99)]
        public double RsiOverbought { get; set; }

        [Parameter("Candele momentum", Group = "Strategia", DefaultValue = 2, MinValue = 1, MaxValue = 5)]
        public int MomentumCandles { get; set; }

        // --- Filtri e volume ---
        [Parameter("Volume (lotti)", Group = "Trading", DefaultValue = 0.01, MinValue = 0.01, Step = 0.01)]
        public double VolumeInLots { get; set; }

        [Parameter("Spread max (pips)", Group = "Trading", DefaultValue = 35, MinValue = 0)]
        public double MaxSpreadPips { get; set; }

        [Parameter("Una posizione alla volta", Group = "Trading", DefaultValue = true)]
        public bool OnePositionOnly { get; set; }

        [Parameter("Solo XAUUSD", Group = "Trading", DefaultValue = true)]
        public bool GoldOnly { get; set; }

        private ExponentialMovingAverage _fastEma;
        private ExponentialMovingAverage _slowEma;
        private RelativeStrengthIndex _rsi;

        protected override void OnStart()
        {
            if (GoldOnly && !Symbol.Name.Contains("XAU"))
            {
                Print("ATTENZIONE: questo bot è pensato per XAUUSD. Simbolo attuale: {0}", Symbol.Name);
            }

            if (Bars.TimeFrame != TimeFrame.Minute)
            {
                Print("ATTENZIONE: timeframe consigliato M1. Attuale: {0}", Bars.TimeFrame);
            }

            _fastEma = Indicators.ExponentialMovingAverage(Bars.ClosePrices, FastEmaPeriod);
            _slowEma = Indicators.ExponentialMovingAverage(Bars.ClosePrices, SlowEmaPeriod);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, RsiPeriod);
        }

        protected override void OnBar()
        {
            if (!CanTrade())
                return;

            if (OnePositionOnly && HasOpenPosition())
                return;

            var signal = GetEntrySignal();
            if (signal == TradeType.Buy)
                OpenScalp(TradeType.Buy);
            else if (signal == TradeType.Sell)
                OpenScalp(TradeType.Sell);
        }

        protected override void OnTick()
        {
            foreach (var position in Positions.FindAll(BotLabel, Symbol.Name))
                ManageTrailingStop(position);
        }

        private bool CanTrade()
        {
            if (MaxSpreadPips > 0 && Symbol.Spread / Symbol.PipSize > MaxSpreadPips)
                return false;

            var minBars = Math.Max(SlowEmaPeriod, RsiPeriod) + MomentumCandles + 2;
            return Bars.Count >= minBars;
        }

        private bool HasOpenPosition()
        {
            return Positions.FindAll(BotLabel, Symbol.Name).Length > 0;
        }

        private TradeType? GetEntrySignal()
        {
            switch (Strategy)
            {
                case EntryStrategy.EmaCrossover:
                    return GetEmaCrossoverSignal();
                case EntryStrategy.RsiReversal:
                    return GetRsiReversalSignal();
                case EntryStrategy.CandleMomentum:
                    return GetCandleMomentumSignal();
                default:
                    return null;
            }
        }

        private TradeType? GetEmaCrossoverSignal()
        {
            var fastPrev = _fastEma.Result.Last(2);
            var slowPrev = _slowEma.Result.Last(2);
            var fastNow = _fastEma.Result.Last(1);
            var slowNow = _slowEma.Result.Last(1);

            if (fastPrev <= slowPrev && fastNow > slowNow)
                return TradeType.Buy;

            if (fastPrev >= slowPrev && fastNow < slowNow)
                return TradeType.Sell;

            return null;
        }

        private TradeType? GetRsiReversalSignal()
        {
            var rsiNow = _rsi.Result.Last(1);
            var rsiPrev = _rsi.Result.Last(2);
            var closeNow = Bars.ClosePrices.Last(1);
            var closePrev = Bars.ClosePrices.Last(2);

            if (rsiPrev < RsiOversold && rsiNow >= RsiOversold && closeNow > closePrev)
                return TradeType.Buy;

            if (rsiPrev > RsiOverbought && rsiNow <= RsiOverbought && closeNow < closePrev)
                return TradeType.Sell;

            return null;
        }

        private TradeType? GetCandleMomentumSignal()
        {
            bool allBullish = true;
            bool allBearish = true;

            for (int i = 1; i <= MomentumCandles; i++)
            {
                var open = Bars.OpenPrices.Last(i);
                var close = Bars.ClosePrices.Last(i);

                if (close <= open)
                    allBullish = false;
                if (close >= open)
                    allBearish = false;
            }

            if (allBullish)
                return TradeType.Buy;
            if (allBearish)
                return TradeType.Sell;

            return null;
        }

        private void OpenScalp(TradeType tradeType)
        {
            var volume = Symbol.QuantityToVolumeInUnits(VolumeInLots);
            var result = ExecuteMarketOrder(tradeType, Symbol.Name, volume, BotLabel, InitialStopLossPips, null);

            if (!result.IsSuccessful)
                Print("Errore apertura {0}: {1}", tradeType, result.Error);
            else
                Print("Apertura {0} @ {1:F2} | SL iniziale {2} pips | Nessun TP", tradeType, result.Position.EntryPrice, InitialStopLossPips);
        }

        private void ManageTrailingStop(Position position)
        {
            if (position.TradeType == TradeType.Buy)
                ManageBuyTrailing(position);
            else
                ManageSellTrailing(position);
        }

        private void ManageBuyTrailing(Position position)
        {
            var entry = position.EntryPrice;
            var currentPrice = Symbol.Bid;
            var profitPips = (currentPrice - entry) / Symbol.PipSize;

            double? targetSl = position.StopLoss;

            // Fase 1: sposta a break-even quando il profitto raggiunge la soglia
            if (BreakEvenTriggerPips > 0 && profitPips >= BreakEvenTriggerPips)
            {
                var bePrice = entry + BreakEvenOffsetPips * Symbol.PipSize;
                if (!targetSl.HasValue || targetSl.Value < bePrice)
                    targetSl = bePrice;
            }

            // Fase 2: trailing stop — SL sempre X pips sotto il prezzo corrente
            if (profitPips > 0)
            {
                var trailPrice = currentPrice - TrailingStopPips * Symbol.PipSize;
                if (!targetSl.HasValue || trailPrice > targetSl.Value)
                    targetSl = trailPrice;
            }

            TryModifyStopLoss(position, targetSl);
        }

        private void ManageSellTrailing(Position position)
        {
            var entry = position.EntryPrice;
            var currentPrice = Symbol.Ask;
            var profitPips = (entry - currentPrice) / Symbol.PipSize;

            double? targetSl = position.StopLoss;

            if (BreakEvenTriggerPips > 0 && profitPips >= BreakEvenTriggerPips)
            {
                var bePrice = entry - BreakEvenOffsetPips * Symbol.PipSize;
                if (!targetSl.HasValue || targetSl.Value > bePrice)
                    targetSl = bePrice;
            }

            if (profitPips > 0)
            {
                var trailPrice = currentPrice + TrailingStopPips * Symbol.PipSize;
                if (!targetSl.HasValue || trailPrice < targetSl.Value)
                    targetSl = trailPrice;
            }

            TryModifyStopLoss(position, targetSl);
        }

        private void TryModifyStopLoss(Position position, double? newStopLoss)
        {
            if (!newStopLoss.HasValue)
                return;

            var currentSl = position.StopLoss;
            if (currentSl.HasValue && Math.Abs(currentSl.Value - newStopLoss.Value) < Symbol.TickSize)
                return;

            // Buy: SL non può essere sopra il bid; Sell: SL non può essere sotto l'ask
            if (position.TradeType == TradeType.Buy && newStopLoss.Value >= Symbol.Bid)
                return;
            if (position.TradeType == TradeType.Sell && newStopLoss.Value <= Symbol.Ask)
                return;

            var result = ModifyPosition(position, newStopLoss, null);
            if (!result.IsSuccessful)
                Print("Errore modifica SL pos {0}: {1}", position.Id, result.Error);
        }
    }

    public enum EntryStrategy
    {
        EmaCrossover,
        RsiReversal,
        CandleMomentum
    }
}
