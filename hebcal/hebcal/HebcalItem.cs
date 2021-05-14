using System;
using System.Text.Json.Serialization;

namespace hebcal
{
    public enum Categories
    {
        none,
        zmanim,
        mevarchim,
        roshchodesh,
        candles,
        havdalah,
        parashat,
        holiday
    }
    public enum SubCategory
    {
        none,
        shabbat,
        minor,
        major,
        fast,
        modern
    }
    public class HebcalItem
    {
        public string title { get; set; }
        public DateTime date { get; set; }
        public Categories? category { get; set; }
        public string title_orig { get; set; }
        public string hebrew { get; set; }
        public Leyning leyning { get; set; }
        public string link { get; set; }
        public string memo { get; set; }
        public SubCategory? subcat { get; set; }

    }
}
