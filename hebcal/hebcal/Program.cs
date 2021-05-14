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
        private const string cleanupDefault = "הַבדָלָה (50 דקות): ,הַדלָקָת נֵרוֹת: ";
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
            var fromMonth = Configuration["fm"] ?? Configuration["from-month"] ?? "9";
            var toMonth = Configuration["tm"] ?? Configuration["to-month"] ?? fromMonth;
            var delimiter = Configuration["d"] ?? Configuration["delimiter"] ?? ",";
            if (delimiter == "\\t" || delimiter == "t")
            {
                delimiter = "\t";
            }
            var output = Configuration["o"] ?? Configuration["output"] ?? Environment.CurrentDirectory;
            var encoding = Configuration["e"] ?? Configuration["encoding"] ?? "utf-16";
            var cleanup = (Configuration["c"] ?? cleanupDefault).Split(',', StringSplitOptions.RemoveEmptyEntries);

            var weeklyOutputFile = Path.Combine(output, $"hebcal-week-{year}-{year + 1}.csv");
            var monthlyOutputFile = Path.Combine(output, $"hebcal-monthly-{year}-{year + 1}.csv");

            Console.WriteLine($@"
years:      01-{fromMonth}-{year} to {DateTime.DaysInMonth(year + 1, int.Parse(toMonth))}-{toMonth}-{year + 1}
delimiter:  {delimiter}
encoding:   {encoding}
file week output:       {weeklyOutputFile}
file monthly output:    {monthlyOutputFile}

started at: {DateTime.Now}
processing...
");
            var sw = new Stopwatch();
            sw.Start();
            HebcalRoot hcl = await GetHebcal(year);
            HebcalRoot hcl2 = await GetHebcal(year + 1);
            hcl.items.AddRange(hcl2.items);

            Console.WriteLine($@"{sw.Elapsed}: data is loaded
prepering data...");
            var hcWeeks = Consolidate(hcl.items, cleanup, year, int.Parse(fromMonth), int.Parse(toMonth));
            Console.WriteLine($@"{sw.Elapsed}: data is ready
writing weekly data...");
            WriteWeekly(delimiter, encoding, weeklyOutputFile, hcWeeks);
            Console.WriteLine($@"{sw.Elapsed}: weekly file is done
File is ready at {weeklyOutputFile}
writing monthly data...");
            WriteMonthly(delimiter, encoding, monthlyOutputFile, hcWeeks);
            Console.WriteLine($@"{sw.Elapsed}: monthly file is done
File is ready at {monthlyOutputFile}");

        }

        private static void WriteMonthly(string delimiter, string encoding, string weeklyOutputFile, List<CalWeek> hcWeeks)
        {
            var sr = new StreamWriter(weeklyOutputFile, false, Encoding.GetEncoding(encoding)) as TextWriter;
            var csv = new CsvHelper.CsvWriter(sr, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.GetCultureInfo("he-IL")) { Delimiter = delimiter });
            WriteHeader(csv);
            var isFirstLine = true;
            foreach (var week in hcWeeks)
            {
                WriteDay(csv, week);
                if (!isFirstLine && HasMonthStartInMiddleOfWeek(week))
                {
                    WriteDay(csv, week);
                }
                isFirstLine = false;
            }
            csv.Flush();
        }

        private static bool HasMonthStartInMiddleOfWeek(CalWeek week)
        {
            var day = week.Days.FirstOrDefault(d => d.date.Day == 1);
            if (day == null)
            {
                return false;
            }
            return day.date.DayOfWeek != DayOfWeek.Sunday;
        }

        private static void WriteWeekly(string delimiter, string encoding, string weeklyOutputFile, List<CalWeek> hcWeeks)
        {
            var sr = new StreamWriter(weeklyOutputFile, false, Encoding.GetEncoding(encoding)) as TextWriter;
            var csv = new CsvHelper.CsvWriter(sr, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.GetCultureInfo("he-IL")) { Delimiter = delimiter });
            WriteHeader(csv);
            foreach (var week in hcWeeks)
            {
                WriteDay(csv, week);
            }
            csv.Flush();
        }

        private static void WriteHeader(CsvHelper.CsvWriter csv)
        {
            for (int i = 1; i < 8; i++)
            {
                csv.WriteField($"day-{i}-date");
                csv.WriteField($"day-{i}-title");
                csv.WriteField($"day-{i}-Month");
                csv.WriteField($"day-{i}-DD");
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
        }

        private static void WriteDay(CsvHelper.CsvWriter csv, CalWeek week)
        {
            foreach (var day in week.Days)
            {
                csv.WriteField(day.date.ToString("yyyy-MM-dd"), true);
                csv.WriteField(day.title, true);
                csv.WriteField(day.date.Month);
                csv.WriteField(day.date.ToString("dd"), true);
                csv.WriteField(day.date.ToString("MM"), true);
                csv.WriteField(day.date.ToString("MMM"), true);
                csv.WriteField(day.date.ToString("MMMM"), true);
                csv.WriteField(day.HebMonth, true);
                csv.WriteField(day.HebMonthDay, true);
                csv.WriteField(day.HebDay, true);
                csv.WriteField(day.HebDayOfWeek, true);
                csv.WriteField(day.HebYear, true);
                csv.WriteField(day.Holiday, true);
                csv.WriteField(day.Parasha, true);
                csv.WriteField(day.Candeles, true);
                csv.WriteField(day.Havdalah, true);
            }
            csv.NextRecord();

        }

        private static List<CalWeek> Consolidate(List<HebcalItem> items, string[] cleanup, int year, int fromMonth, int toMonth)
        {
            var dic = new Dictionary<DateTime, HebItem>();
            var hc = new HebrewCalendar();
            CultureInfo cl = CultureInfo.CreateSpecificCulture("he-IL");
            cl.DateTimeFormat.Calendar = cl.Calendar;
            cl.DateTimeFormat.Calendar = hc;

            var startDate = new DateTime(year, fromMonth, 1);
            while (startDate.DayOfWeek != DayOfWeek.Sunday)
            {
                startDate = startDate.AddDays(-1);
            }
            var endDate = new DateTime(year + 1, toMonth, 1);
            endDate = endDate.AddMonths(1);
            for (DateTime d = startDate; d < endDate; d = d.AddDays(1))
            {
                dic.Add(d, new HebItem
                {
                    date = d,
                    HebYear = d.ToString("yyyy", cl),
                    HebMonth = d.ToString("MMM", cl),
                    HebDay = d.ToString("dddd", cl).Replace("יום ", string.Empty),
                    HebMonthDay = d.ToString("dd", cl),
                    HebDayOfWeek = d.ToString("ddd", cl).Replace("יום ", string.Empty)
                });
            }
            items
                .Where(item => item.date.Date >= startDate && item.date.Date < endDate)
                .ToList()
                .ForEach(item =>
                  {
                      if (!dic.ContainsKey(item.date.Date))
                      {
                          return;
                      }
                      var ni = dic[item.date.Date];

                      ni.title += ", " + item.title;
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

            var days = dic
                .Select(kv =>
                {
                    kv.Value.title = RunCleanup(cleanup, kv.Value.title);
                    kv.Value.Candeles = RunCleanup(cleanup, kv.Value.Candeles);
                    kv.Value.Havdalah = RunCleanup(cleanup, kv.Value.Havdalah);
                    kv.Value.Parasha = RunCleanup(cleanup, kv.Value.Parasha);
                    return kv.Value;
                })
                .OrderBy(v => v.date)
                .ToList();
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

        private static string RunCleanup(string[] cleanup, string t)
        {
            if (string.IsNullOrEmpty(t))
            {
                return t;
            }
            foreach (var c in cleanup)
            {
                t = t.Replace(c, string.Empty);
            }

            return t.Trim(',', ' ');
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
fm, from-month - month to start from [default: 9]
tm, to-month - month to end on (end of month of next year) [default:  from-month]
d, delimiter - [default: ,] t|\t = tab
o, output - folder path to place the result file
e, encoding - output encoding [default: utf-16]
c - title cleanup [""{cleanupDefault}""]
");
                        Environment.Exit(0);
                        break;
                }
            }
        }
    }
}
