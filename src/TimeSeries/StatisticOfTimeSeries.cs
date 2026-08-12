using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STEDI.Series
{
    public class StatisticOfTimeSeries
    {
        public DateTime Start {  get; set; } = DateTime.MinValue;
        public DateTime End { get; set; } = DateTime.MaxValue;
        public DateTime StartSeasonIgnoreYear { get; set; } = new DateTime(2000, 1, 1);
        public DateTime EndSeasonIgnoreYear { get; set; } = new DateTime(2000, 12, 31);
    }
}
