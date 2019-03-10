using System;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace OneBuilding_EPW
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var regions = new string[]{
                "WMO_Region_1_Africa",
                "WMO_Region_2_Asia",
                "WMO_Region_3_South_America",
                "WMO_Region_4_North_and_Central_America",
                "WMO_Region_5_Southwest_Pacific",
                "WMO_Region_6_Europe",
                "WMO_Region_7_Antarctica"
            };
            var LocationPatten = "<td><a href=\\\"([a-zA-Z0-9-._/]+?)\\.html";
            var zipFilePatten = "href=\\\"([a-zA-Z0-9-._/]+?)\\.zip";
            
            var link = @"http://climate.onebuilding.org/";

            var selectedRegions = GetUsersChoice(regions);
        
            var dir = Directory.GetCurrentDirectory();

            var fd = Path.Combine(dir, "OneBuilding_EPW");
            Directory.CreateDirectory(fd);

            var regionsUrls = selectedRegions.Select(_ => link + _ );
            var locationUrls = ExtractInfo(regionsUrls, LocationPatten).Select(_ => _.Replace("/index.html","")); ;
            var zipUrls = ExtractInfo(locationUrls, zipFilePatten);


            var tempFd = fd + "/temp";
            if (Directory.Exists(tempFd)) Directory.Delete(tempFd, true);
            Directory.CreateDirectory(tempFd);
            var csvStrings = new List<string>() { "Name,Lat,Lon,Url"};

            //var testZips = zipUrls.Take(10);

            foreach (var url in zipUrls)
            {
                Console.WriteLine($"Now thread {System.Threading.Thread.CurrentThread.ManagedThreadId} extracting: \n\t{url.Split("/").Last()}\n");
                var result = DownloadAndRead(url, tempFd);
                if (!string.IsNullOrEmpty(result.name))
                {
                    csvStrings.Add($"{result.name},{result.Lat},{result.Lon},{url}");
                }
            }
            //Parallel.ForEach(zipUrls, (url) =>
            //{
            //    Console.WriteLine($"Now thread {System.Threading.Thread.CurrentThread.ManagedThreadId} extracting: \n\t{url.Split("/").Last()}\n");
            //    var result = DownloadAndRead(url, tempFd);
            //    if (!string.IsNullOrEmpty( result.name))
            //    {
            //        csvStrings.Add($"{result.name},{result.Lat},{result.Lon},{url}");
            //    }
                
            //});

          


            var EpwZipUrlsCsv = Path.Combine(fd, "EpwZipUrls.csv");
            File.Delete(EpwZipUrlsCsv);
            var csv = new StringBuilder();

            csv.AppendJoin(Environment.NewLine, csvStrings);
            File.WriteAllText(EpwZipUrlsCsv, csv.ToString());


            Console.WriteLine("\n\nProcessing complete. Press any key to exit.");
            Console.WriteLine($"Check the folder:{fd}");
            
            Console.ReadKey();
            
        }

        public static IEnumerable<string> GetUsersChoice(IEnumerable<string> Options)
        {
            Console.Clear();
            var results = new List<string>();
            Console.WriteLine("Enter a number to select a region. Type 9 for all.");
            for (int i = 0; i < Options.Count(); i++)
            {
                Console.WriteLine($"\t[{i}] - {Options.ElementAt(i)}");
            }
            var userInput = Console.ReadKey();
            int regionIndex = char.IsDigit(userInput.KeyChar) ? int.Parse(userInput.KeyChar.ToString()) : -1;
       
            if (regionIndex >= 0 && regionIndex < Options.Count())
            {
                var opt = Options.ElementAt(regionIndex);
                Console.WriteLine($"\nYou selected {opt}, correct? [Y or N]");
                var yon = Console.ReadKey();
                if (yon.Key == ConsoleKey.Y || yon.Key == ConsoleKey.Enter)
                {
                    results.Add(opt);
                }
                else
                {
                    return GetUsersChoice(Options);
                }

            }
            else if (regionIndex == 9)
            {
                Console.WriteLine($"\nYou selected ALL, correct? [Y or N]");
             
                var yon = Console.ReadKey();
                if (yon.Key == ConsoleKey.Y || yon.Key == ConsoleKey.Enter)
                {
                    results.AddRange(Options);
                }
                else
                {
                    return GetUsersChoice(Options);
                }
            }
            else
            {
                return GetUsersChoice(Options);
            }
            

            return results;

        }

        public static IEnumerable<string> ExtractInfo(IEnumerable<string> Urls, string RegexPatten)
        {
            using (WebClient webClient = new WebClient())
            {
                var list = new List<string>();

                foreach (var Url in Urls)
                {
                    Console.WriteLine($"Now processing:\n\t{Url}\n");
                    try
                    {
                        string htmlCode = webClient.DownloadString(Url);
                        var result = from Match match in Regex.Matches(htmlCode, RegexPatten)
                                     select $"{Url}/{match.ToString().Split("\"")[1]}";
                        list.AddRange(result);
                    }
                    catch (WebException)
                    {

                        //throw;
                    }
                    
                }
               
                return list;
            }
        }

        public static (string name, double Lat, double Lon) DownloadAndRead(string ZipUrl, string folder)
        {
            var link = ZipUrl;
            var name = link.Split("/").Last();
    
            var temp = Path.Combine(folder, name);

            using (WebClient webClient = new WebClient())
            {
                webClient.DownloadFile(new Uri(link), temp);
                if (File.Exists(temp))
                {
                    var result = ReadZip(temp);

                    //File.Delete(temp);
                    return (name, result.Lat, result.Lon);
                }
                else
                {
                    return ("", -1, -1);
                }
            }

           
        }

        public static (double Lat, double Lon) ReadZip(string ZipFilePath)
        {
            string zipFile = ZipFilePath;
            var lat = -1.0;
            var lon = -1.0;
            using (StreamReader stream = new StreamReader(ZipFile.OpenRead(zipFile)
                         .Entries.Where(x => x.Name.EndsWith("stat"))
                         .FirstOrDefault().Open(), Encoding.UTF8))
            {
                var headers = ReadFirstFewLines(stream);
                var coordStrings = ReadInfo(headers);

                lat = StringToCoordinates(coordStrings.Lat);
                lon = StringToCoordinates(coordStrings.Lon);

                stream.Dispose();
               
            }

            //File.Delete(zipFile);
            return (lat, lon);

            string ReadFirstFewLines(System.IO.StreamReader streamR)
            {
                int line = 5;
                int count = 0;

                char nextChar;
                StringBuilder sline = new StringBuilder();
                while (streamR.Peek() > 0)
                {
                    nextChar = (char)streamR.Read();
                    if (nextChar == '\n')
                    {
                        if (count > line)
                        {
                            return sline.ToString();
                        }
                        else
                        {
                            count++;
                        }
                    }

                    sline.Append(nextChar);
                }

                return sline.Length == 0 ? null : sline.ToString();
            }

            //-EnergyPlus Weather Converter Version 2018.10.01
            //Statistics for DZA_AD_Miliana.604300_TMYx.2003 - 2017
            //Location-- Miliana AD DZA
            //{ N 36� 18.00'} {E 2� 13.98'}
            //{ GMT + 0.0 Hours}
            //Elevation--   721 m above sea level
            //Standard Pressure at Elevation --92958 Pa
            //Data Source-- ISD - TMYx

            (string Lat, string Lon) ReadInfo(string stringBlock)
            {
                var result = from Match match in Regex.Matches(stringBlock, "\\{.*?\\}")
                             select match.ToString();

                var lati = string.Empty;
                var lont = string.Empty;

                if (result.Count() >= 2)
                {
                    lati = result.ElementAt(0);
                    lont = result.ElementAt(1);
                }

                return (lati, lont);
            }

            double StringToCoordinates(string CoordString)
            {
                var result = from Match match in Regex.Matches(CoordString, "[\\d.]+")
                             select Convert.ToDouble(match.ToString());

                double coord = -1;
                if (result.Count() == 2)
                {
                    coord = result.ElementAt(0)+ result.ElementAt(1)/60;
                }
                return coord;
            }
        }
    }


}