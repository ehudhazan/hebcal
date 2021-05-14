using System;
using System.Collections.Generic;

namespace hebcal
{
    public class HebcalRoot
    {
        public string title { get; set; }
        public DateTime date { get; set; }
        public Location location { get; set; }
        public List<HebcalItem> items { get; set; }
    }
}
