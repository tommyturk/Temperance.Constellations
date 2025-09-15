using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Temperance.Services.BackTesting.Interfaces
{
    public interface ISleeveSelectionOrchestrator
    {
        Task SelectInitialSleeve(Guid sessionId, DateTime inSampleEndDate);
        Task ReselectAnnualSleeve(Guid sessionId, DateTime yearEnd);

    }
}
