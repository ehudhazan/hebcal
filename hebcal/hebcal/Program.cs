using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace hebcal
{
    public static class Program
    {
        public static IConfiguration Configuration { get; private set; }
        static async Task Main(string[] args)
        {
            CheckArgs(args);
            Configuration = new ConfigurationBuilder()
                .AddCommandLine(args)
                .Build();

            if (!int.TryParse(Configuration["year"], out int year))
            {
                year = DateTime.Now.Year;
            }
            var delimiter = Configuration["d"] ?? Configuration["delimiter"] ?? ",";
            if (delimiter == "\\t" || delimiter == "t")
            {
                delimiter = "\t";
            }
            var output = Configuration["o"] ?? Configuration["output"] ?? Environment.CurrentDirectory;
            var encoding = Configuration["e"] ?? Configuration["encoding"] ?? "utf-16";

            var outputFile = Path.Combine(output, $"hebcal-{year}-{year + 1}.csv");

            Console.WriteLine($@"
years:\t{year} to {year + 1}
delimiter:\t{delimiter}
encoding:\t{encoding}
file output:\t{outputFile}

started at:\t{DateTime.Now}
processing...
");
            var sw = new Stopwatch();
            sw.Start();
            HebcalRoot hcl = await GetHebcal(year);
            HebcalRoot hcl2 = await GetHebcal(year + 1);
            hcl.items.AddRange(hcl2.items);

            Console.WriteLine($@"{sw.Elapsed}: data is loaded
prepering data...");
            var hcWeeks = Consolidate(hcl.items);
            Console.WriteLine($@"{sw.Elapsed}: data is ready
writing data...");
            using var sr = new StreamWriter(outputFile, false, Encoding.GetEncoding(encoding)) as TextWriter;
            using var csv = new CsvHelper.CsvWriter(sr, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.GetCultureInfo("he-IL")) { Delimiter = delimiter });

            for (int i = 1; i < 7; i++)
            {
                csv.WriteField($"day-{i}-date");
                csv.WriteField($"day-{i}-title");
                csv.WriteField($"day-{i}-Month");
                csv.WriteField($"day-{i}-MM");
                csv.WriteField($"day-{i}-MMM");
                csv.WriteField($"day-{i}-MMMM");
                csv.WriteField($"day-{i}-HebMonth");
                csv.WriteField($"day-{i}-HebMonthDay");
                csv.WriteField($"day-{i}-HebDay");
                csv.WriteField($"day-{i}-HebDayOfWeek");
                csv.WriteField($"day-{i}-HebYear");
                csv.WriteField($"day-{i}-Holiday");
                csv.WriteField($"day-{i}-Parasha");
                csv.WriteField($"day-{i}-Candeles");
                csv.WriteField($"day-{i}-Havdalah");
            }
            csv.NextRecord();
            foreach (var week in hcWeeks)
            {
                WriteDay(csv, week);
                if (week.Days[0].date.Day != 1)
                {
                    WriteDay(csv, week);
                }
            }
            sw.Stop();
            Console.WriteLine($@"{sw.Elapsed}: done
File is ready at {outputFile}");
        }

        private static void WriteDay(CsvHelper.CsvWriter csv, CalWeek week)
        {
            foreach (var day in week.Days)
            {
                csv.WriteField(day.date.ToString("yyyy-MM-dd"));
                csv.WriteField(day.title);
                csv.WriteField(day.date.Month);
                csv.WriteField(day.date.ToString("MM"));
                csv.WriteField(day.date.ToString("MMM"));
                csv.WriteField(day.date.ToString("MMMM"));
                csv.WriteField(day.HebMonth);
                csv.WriteField(day.HebMonthDay);
                csv.WriteField(day.HebDay);
                csv.WriteField(day.HebDayOfWeek);
                csv.WriteField(day.HebYear);
                csv.WriteField(day.Holiday);
                csv.WriteField(day.Parasha);
                csv.WriteField(day.Candeles);
                csv.WriteField(day.Havdalah);
            }
            csv.NextRecord();

        }

        private static List<CalWeek> Consolidate(List<HebcalItem> items)
        {
            var dic = new Dictionary<DateTime, HebItem>();
            var hc = new HebrewCalendar();
            CultureInfo cl = CultureInfo.CreateSpecificCulture("he-IL");
            cl.DateTimeFormat.Calendar = cl.Calendar;
            cl.DateTimeFormat.Calendar = hc;

            var startDate = items.Min(item => item.date).Date;
            while (startDate.DayOfWeek != DayOfWeek.Sunday)
            {
                startDate = startDate.AddDays(-1);
            }
            var endDate = items.Max(item => item.date).Date.AddDays(1);
            for (DateTime d = startDate; d < endDate; d = d.AddDays(1))
            {
                dic.Add(d, new HebItem
                {
                    date = d,
                    HebYear = d.ToString("yyyy", cl),
                    HebMonth = d.ToString("MMM", cl),
                    HebDay = d.ToString("dddd", cl),
                    HebMonthDay = d.ToString("dd", cl),
                    HebDayOfWeek = d.ToString("ddd", cl).Replace("יום ", string.Empty)
                });
            }
            items.ForEach(item =>
            {
                var ni = dic[item.date.Date];

                ni.title = item.title == null ? item.title : ni.title + ", " + item.title;

                switch (item.category)
                {
                    case Categories.roshchodesh:
                        ni.IsRoshchodesh = true;
                        break;
                    case Categories.candles:
                        ni.Candeles = item.title;
                        break;
                    case Categories.havdalah:
                        ni.Havdalah = item.title;
                        break;
                    case Categories.parashat:
                        ni.Parasha = item.title;
                        break;
                    case Categories.holiday:
                        ni.Holiday = item.title;
                        break;
                    default:
                        break;
                }


            });

            var days = dic.Select(kv => kv.Value).OrderBy(v => v.date).ToList();
            var res = new List<CalWeek>();
            var w = new CalWeek();
            foreach (var d in days)
            {
                if (d.date.DayOfWeek == DayOfWeek.Sunday)
                {
                    w = new CalWeek();
                    res.Add(w);
                }
                w.Days.Add(d);
            }
            return res;
        }

        private static async Task<HebcalRoot> GetHebcal(int year)
        {
            var fn = $"hebcal-{year}.json";
            var jo = new JsonSerializerOptions();
            jo.Converters.Add(new JsonStringEnumConverter());
            if (File.Exists(fn))
            {
                try
                {
                    Console.WriteLine($@"getting data from cache for year: {year}");
                    return JsonSerializer.Deserialize<HebcalRoot>(File.ReadAllText(fn), jo);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"failed to load file {fn}, {ex}");
                }
            }
            Console.WriteLine($@"getting data from server for year: {year}");
            var hc = HttpClientFactory.Create();
            var res = await hc.GetStringAsync($"https://www.hebcal.com/hebcal?v=1&cfg=json&maj=on&min=on&mod=on&nx=on&year={year}&month=x&ss=on&mf=on&c=on&geo=geoname&geonameid=293397&m=50&s=on&lg=h");
            var hcl = JsonSerializer.Deserialize<HebcalRoot>(res, jo);
            if (File.Exists(fn))
            {
                File.Delete(fn);
            }
            File.WriteAllText(fn, res);
            return hcl;
        }

        private static void CheckArgs(string[] args)
        {
            foreach (var arg in args)
            {
                switch (arg)
                {
                    case "-h":
                    case "--help":
                        Console.WriteLine($@"
ex 1: hebcal year={DateTime.Now.Year}
ex 2: hebcal year={DateTime.Now.Year} d=\t o=""{Environment.CurrentDirectory}"" e=utf-16

-h, --help - to get this help
year - the year to start from [default={DateTime.Now.Year}]
d, delimiter - [default: ,] t|\t = tab
o, output - folder path to place the result file
e, encoding - output encoding [default: utf-16]
");
                        Environment.Exit(0);
                        break;
                }
            }
        }
    }
}
