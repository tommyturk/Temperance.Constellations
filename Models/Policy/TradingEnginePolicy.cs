namespace Temperance.Constellations.Models.Policy
{
    public static class TradingEnginePolicy
    {
        // =========================================================================
        // 1. RISK & VOLATILITY GUARDRAILS (Ludus handles the actual targets)
        // =========================================================================
        /// <summary> Moderated: 0.12m (Up from 0.10, down from 0.20). </summary>
        public const decimal ANNUAL_VOLATILITY_TARGET_FLOOR_PERCENT = 0.12m;

        /// <summary> Moderated: 0.35m (Up from 0.25, down from 0.45). </summary>
        public const decimal ANNUAL_VOLATILITY_TARGET_CEILING_PERCENT = 0.35m;

        public const decimal PORTFOLIO_DIVERSIFICATION_MULTIPLIER_IDM = 1.4m;
        public const decimal TARGET_DIVERSIFIED_ASSET_COUNT = 20.0m;

        // =========================================================================
        // 2. REGIME MULTIPLIERS (The "Downshift" Logic)
        // =========================================================================
        public const decimal REGIME_MULTIPLIER_STRONGLY_BULLISH = 1.25m;
        public const decimal REGIME_MULTIPLIER_BULLISH = 1.00m;

        /// <summary> Moderated: 0.70m (Middle ground). </summary>
        public const decimal REGIME_MULTIPLIER_NEUTRAL = 0.70m;

        /// <summary> Moderated: 0.45m (Respecting the bear, but still hunting). </summary>
        public const decimal REGIME_MULTIPLIER_BEARISH = 0.45m;

        /// <summary> Moderated: 0.25m (Toe in the water, not a foot). </summary>
        public const decimal REGIME_MULTIPLIER_STRONGLY_BEARISH = 0.25m;

        // =========================================================================
        // 3. SECTOR & CAPACITY CONSTRAINTS
        // =========================================================================

        /// <summary> Moderated: 0.45m (Split the difference). </summary>
        public const decimal SECTOR_VOLATILITY_MAX_EXPOSURE_RATIO = 0.45m;

        /// <summary> Moderated: 0.20m (Institutional pain limit). </summary>
        public const decimal MAX_TRANSACTION_COST_TO_ATR_RATIO_THRESHOLD = 0.20m;

        public const decimal MAX_SINGLE_POSITION_EQUITY_EXPOSURE_PERCENT = 0.15m;
    }
}

