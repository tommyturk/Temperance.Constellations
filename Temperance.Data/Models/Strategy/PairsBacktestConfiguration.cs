using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Temperance.Data.Models.Strategy
{
    public class PairsBacktestConfiguration
    {
        public string StrategyName { get; set; }

        public Dictionary<string, object> StrategyParameters { get; set; }

        public List<PairDefinition> PairsToTest { get; set; }

        public string Interval { get; set; }
        
        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public decimal InitialCapital { get; set; } = 10000;

        public int MaxParallelism { get; set; } = Environment.ProcessorCount / 4;
    }
}
