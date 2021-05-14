using System;

namespace hebcal
{
    public class HebItem
    {
        internal DateTime date;

        public string title { get; set; }
        public bool IsRoshchodesh { get; set; }
        public string Candeles { get; internal set; }
        public string Havdalah { get; internal set; }
        public string Parasha { get; internal set; }
        public string Holiday { get; internal set; }
        public string HebMonth { get; internal set; }
        public string HebYear { get; internal set; }
        public string HebDay { get; internal set; }
        public string HebMonthDay { get; internal set; }
        public string HebDayOfWeek { get; internal set; }
    }
}
