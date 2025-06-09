using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Temperance.Data.Models.Strategy
{
    public class PairDefinition
    {
        public string SymbolA { get; set; }
        public string SymbolB { get; set; }
        public decimal HedgeRation { get; set; }
    }
}
