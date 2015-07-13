///
/// Aircraft Telemetry/ weather reporting simulation. This application utilizes the Bing Maps API and you will need
/// a Bing Maps API key (free for development) http://www.microsoft.com/maps/choose-your-bing-maps-API.aspx . You 
/// will also need an Service Bus connection string and an Azure Storage account and key (stored in your App.config 
/// file). 
/// 
/// This application plots a course from point a to point b and sends simulated telemetry data to Azure Event Hubs
/// (a component of Azure Service Bus). Azure Stream Analytics picks up the "telemetry" and places it in an Azure
/// Table. Subsequent flights can read the Azure Table to see if there is weather along their flight paths that might
/// affect them.
/// 
/// It also uses some very primitive icons for the weather on the map which I believe are licensed under Creative
/// Commons (retrieved via PowerPoint's image search). For a production application you should pick something better. 
/// 
/// (c) Barry Briggs 2015 MIT License
/// 


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions; 
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Configuration;
using System.Collections;
using System.Runtime.Serialization;
using System.Net;
using System.IO;
using System.Web; 
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Microsoft.Maps.MapControl.WPF;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;

namespace Plane
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 
    public class AirTelemetry : TableEntity
    {
        public DateTimeOffset dto { get; set; }
        public string inflight { get; set; }
        public string airline { get; set; }
        public string flight { get; set; }
        public double lat { get; set; }
        public double lng { get; set; }
        public double heading { get; set; }
        public double airspeed { get; set; }
        public double altitude { get; set; }
        public DateTime arrival { get; set; }
        public double temp { get; set; }
        public double windshear { get; set; }
        public double ice { get; set; }
        public double windspeed { get; set; }
        public double lightning { get; set; }
        public AirTelemetry() { }

    }
    public enum WeatherEventTypes
    {
        LIGHT_ICE,
        MODERATE_ICE,
        HEAVY_ICE,
        MODERATE_TURB,
        HEAVY_TURB,
        TSTORM,
        MODERATE_SHEAR,
        HEAVY_SHEAR

    }
    public class WeatherEvent
    {
        public WeatherEventTypes eventtype { get; set; }
        public DateTimeOffset timerecorded { get; set; }
        public Location location { get; set;  }
        public WeatherEvent() { }
        public WeatherEvent(WeatherEventTypes wet, DateTimeOffset t, Location l) { eventtype = wet; timerecorded = t; location = l; }

    }

    public partial class MainWindow : Window
    {
        static string eventHubName = "air";
        static private string airline = "";
        static private string flight = "";
        static private string origin = "";
        static private string destination = "";
        static private string bingKey = "Avlqj0AS9U01R4TruzJRi67-eAIPn7oDT8Z7Wv0TTCJ9IXK1U_hERc4GGjCT2wvS";
        static private Dictionary<string, string> airports = new Dictionary<string, string>(); 

        /* weather conditions */
        private bool ltice = false;
        private bool mdice = false;
        private bool hvice = false;
        private bool mdtub = false;
        private bool hvtub = false;
        private bool tstrm = false;
        private bool mdshr = false;
        private bool hvshr = false;

        private double miles; 

        /* for debugging the display; use database next */
        private List<WeatherEvent> weatherevents = new List<WeatherEvent>();

        private bool tableinitialized = false;
        CloudTableClient tableClient = null;
        CloudTable table = null; 


        public MainWindow()
        {
            InitializeComponent();



            airports.Add("BOS", "Boston, MA");
            airports.Add("LAX", "Los Angeles, CA");
            airports.Add("SFO", "San Franciso, CA");
            airports.Add("SEA", "Seattle, WA");
            airports.Add("IAD", "Washington, DC Dulles");
            airports.Add("JFK", "New York City, NY");
            airports.Add("IAH", "Houston, TX");
            airports.Add("MSY", "New Orleans, LA"); 
/*
            string exepath = System.IO.Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]);
            const Int32 BufferSize = 128;
            using (var fileStream = File.OpenRead("airport-codes.csv"))
            {
                using (var streamReader = new StreamReader(fileStream, Encoding.UTF8, true, BufferSize))
                {
                    String line;
                    while ((line = streamReader.ReadLine()) != null)
                    {
                        int i1 = line.IndexOf('"');
                        int i2 = line.IndexOf('"', i1 + 1);
                        string city = line.Substring(i1 + 1, i2 - 1);
                        string code = line.Substring(i2 + 2, line.Length - (i2 + 2));
                        try
                        {
                            airports.Add(code, city);
                        }
                        catch(Exception e)
                        {

                        }
                    }
                }
            }
 */ 
 
        }

        async private void StartFlight_Click(object sender, RoutedEventArgs e)
        {
            airline=AirlineNameBox.Text; ;
            flight=AirlineFlightNumberBox.Text;

            origin=DepartureCityBox.Text;
            destination=ArrivalCityBox.Text;

            origin = origin.ToUpper();
            destination = destination.ToUpper(); 

            if (string.IsNullOrEmpty(airline) ||
               string.IsNullOrEmpty(flight) ||
               string.IsNullOrEmpty(origin) ||
               string.IsNullOrEmpty(destination))
            {
                FlightInfoBox.Content = "Missing data, please re-enter";
                return;
            }

            if(origin.Length != 3 || destination.Length!=3)
            {
                FlightInfoBox.Content = "Cities not encoded properly, use 3-letter airport code";
            }

            double originlat = 0.0;
            double originlng = 0.0;
            double destlat = 0.0;
            double destlong = 0.0;

            string origincity = airports[origin];
            string destcity = airports[destination];

            int error = 0;
            int erro2 = 0; 

            error=GetLatLongFromName(origincity, out originlat, out originlng);
            error=GetLatLongFromName(destcity, out destlat, out destlong);
            /* possible errors on Bing lookup:
             *  call error == -1
             *  if both lat and long of a lookup are 0.0 then Bing couldn't resolve the name (fix
             *  the airport-codes.csv file)
             */
            if(error<0 || erro2<0 || (originlat==0.0&&originlng==0.0) || (destlat==0.0 && destlong==0.0))
            {
                FlightInfoBox.Content = "Error; please try again.";
                return;
            }

            /* get distance and heading */
            Location o = new Location(originlat, originlng);
            Location d = new Location(destlat, destlong);
            double distance = Haversine.HaversineDistance(o, d);
            miles = distance * 0.00062137;
            double heading = Haversine.Bearing(o, d);

            /* create the packet */
            AirTelemetry AT = new AirTelemetry();
            AT.PartitionKey = "airtelemetry";
            AT.RowKey = airline.ToLower() + flight;
            AT.airline = airline;
            AT.flight = flight;
            AT.lat = o.Latitude;
            AT.lng = o.Longitude;
            AT.heading = heading;
            AT.airspeed = 0.0;
            AT.temp = 38.0;
            AT.ice = 0.0;
            AT.lightning = 0.0;
            AT.windshear = 0.0;
            AT.windspeed = 15.0;
            AT.arrival = DateTime.Now;
            UpdateFlightInfoBox(AT, false); 
            if (CreateEventHub())
            {
                await TrackFlight(AT, o, d, miles);
            }
        }
        private void UpdateFlightInfoBox(AirTelemetry at, bool arrived)
        {
            double compassheading = at.heading < 0 ? at.heading + 360.0 : at.heading; 
            FlightInfoBox.Content = "Flight " + airline + " " + flight + " travelling " + miles.ToString("N0") + " miles on a heading of " + at.heading.ToString("N0")+ " degrees";
            if (arrived)
                FlightInfoBox.Content += " Flight has arrived."; 
        }
        async Task TrackFlight(AirTelemetry AT, Location origin, Location destination, double miles)
        {
            string json="";
            var eventHubConnectionString = GetEventHubConnectionString();
            var eventHubClient = EventHubClient.CreateFromConnectionString(eventHubConnectionString, eventHubName);
            int runningMileCount = 0;
            int updateWeatherInverval = 200;
            int updateWeatherRadius = 300; 

            /* how many miles we go per tick */
            int mileageIncrement = 30;

            Pushpin pin = new Pushpin();
            Label infoLabel = new Label(); 
            MapLayer InfoLayer = new MapLayer();

            AT.RowKey += DateTime.Now.ToShortDateString();
            /* get rid of illegal characters in row key */
            AT.RowKey = Regex.Replace(AT.RowKey, @"[\ /?#]", "");

            MapPolyline polyline = new MapPolyline();
            polyline.Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Blue);
            polyline.StrokeThickness = 5;
            polyline.Opacity = 0.7;

            /* pre-plot the course so that we can draw the flight path on the map. we recalculate the bearing every
             * 100 miles (could/should be configurable) to get a true Great Circle route. */
            Location prev = origin;
            Location nxtl;
            double hdg = AT.heading; 
            polyline.Locations=new LocationCollection();
            polyline.Locations.Add(origin); 
            int j;
            for (j = 2; ;j++ )
            {
                nxtl = Haversine.PositionDistanceFromOrigin(prev, mileageIncrement / 0.00062137, hdg); 
                polyline.Locations.Add(nxtl);
                hdg = Haversine.Bearing(nxtl, destination);
                prev = nxtl;
                if (Haversine.InRange(nxtl, destination.Latitude, destination.Longitude, 100))
                {
                    polyline.Locations.Add(destination);
                    break;
                }

            }
            string rkey = AT.RowKey;
            AT.lat = origin.Latitude;
            AT.lng = origin.Longitude;
            UpdateMap(AT, pin, polyline, InfoLayer);
            UpdateWeatherFromTable(origin, 300, new TimeSpan(3, 0, 0));

            int i; 

            for (i = 0; i <= polyline.Locations.Count; i++)
            {
                await Task.Delay(2000);
                try
                {
                    AT.RowKey = rkey +'-'+ i.ToString();          /* row key must be unique or will overwrite existing */ 
                    FlightMap.Children.Clear();
                    FlightMap.Children.Add(polyline);

                    InfoLayer.Children.Clear();
                    /* we have two time stamps. the first is inherited from TableEntity and normally should not be used by applications. We
                     * initialize it here because Stream Analytics will barf on an illegal data (dates must be greater than 
                     * 12:00 midnight, January 1, 1601 A.D. (C.E.), UTC.). We do not use the inherited time stamp after this; this is what
                     * AT.dto is for */ 
                    AT.Timestamp = DateTime.Now;
                    AT.dto = DateTimeOffset.Now; 
                    AT.inflight = "true";
                    /* send distance in meters */
                    Location p = Haversine.PositionDistanceFromOrigin(new Location(AT.lat, AT.lng), mileageIncrement / 0.00062137, AT.heading); 
                    AT.lat = p.Latitude;
                    AT.lng = p.Longitude;

                    double b2 = Haversine.Bearing(p, destination);
                    AT.heading = b2; 

                    /* altitude in meters */
                    if (i < 5)
                        AT.altitude = 10000 / (6 - (i + 1)); /* ascending */
                    else if ((polyline.Locations.Count - i) < 5)
                        AT.altitude = 10000 / (polyline.Locations.Count+1 - i); /* descending */
                    else
                        AT.altitude = 10000;

                    /* initialize the data structure */ 
                    AT.ice = 0.0;
                    AT.windspeed = 0.0;
                    AT.windshear = 0.0;
                    AT.lightning = 0.0; 

                    /* encode weather conditions */
                    if(ltice)
                    {
                        AT.ice = 5.0;
                        WeatherEvent we = new WeatherEvent(WeatherEventTypes.LIGHT_ICE, DateTime.Now, p);
                        weatherevents.Add(we); 
                        ltice = false;
                    }
                    else if(mdice)
                    {
                        AT.ice = 10.0;
                        WeatherEvent we = new WeatherEvent(WeatherEventTypes.MODERATE_ICE, DateTime.Now, p);
                        weatherevents.Add(we); 
                        mdice = false; 
                    }
                    else if (hvice)
                    {
                        AT.ice = 20.0;
                        WeatherEvent we = new WeatherEvent(WeatherEventTypes.HEAVY_ICE, DateTime.Now, p);
                        weatherevents.Add(we); 
                        hvice = false; 
                    }
                    if(mdtub)
                    {
                        AT.windspeed = 20.0;
                        WeatherEvent we = new WeatherEvent(WeatherEventTypes.MODERATE_TURB, DateTime.Now, p);
                        weatherevents.Add(we); 
                        mdtub = false;
                    }
                    else if(hvtub)
                    {
                        AT.windspeed = 40.0;
                        WeatherEvent we = new WeatherEvent(WeatherEventTypes.HEAVY_TURB, DateTime.Now, p);
                        weatherevents.Add(we); 
                        hvtub = false; 
                    }
                    if(mdshr)
                    {
                        AT.windshear = 20.0;
                        WeatherEvent we = new WeatherEvent(WeatherEventTypes.MODERATE_SHEAR, DateTime.Now, p);
                        weatherevents.Add(we); 
                        mdshr = false;
                    }
                    else if(hvshr)
                    {
                        AT.windshear = 40.0;
                        WeatherEvent we = new WeatherEvent(WeatherEventTypes.HEAVY_SHEAR, DateTime.Now, p);
                        weatherevents.Add(we); 
                        hvshr = false; 
                    }
                    if(tstrm)
                    {
                        AT.lightning = 50.0;
                        WeatherEvent we = new WeatherEvent(WeatherEventTypes.TSTORM, DateTime.Now, p);
                        weatherevents.Add(we); 
                        tstrm = false; 
                    }

                    json = Newtonsoft.Json.JsonConvert.SerializeObject(AT);
                    Console.WriteLine("{0} > Sending message: {1}", DateTime.Now.ToString(), json);

                    FlightMap.Center.Latitude = p.Latitude;
                    FlightMap.Center.Longitude = p.Longitude;
                    FlightMap.Center.Altitude = AT.altitude; 
                    Location center = FlightMap.Center;
                    double zoom = FlightMap.ZoomLevel; 
                    FlightMap.SetView(center, zoom);

                    pin.Location = center;
                    FlightMap.Children.Add(pin);

                    infoLabel.Content = AT.airline + " " + AT.flight;
                    InfoLayer.AddChild(infoLabel, center);
                    WeatherImageLayer(InfoLayer);
                    FlightMap.Children.Add(InfoLayer);

                    UpdateFlightInfoBox(AT, false); 

                    if(Haversine.InRange(new Location(AT.lat, AT.lng), destination.Latitude,destination.Longitude,50))
                        break; 

                    await eventHubClient.SendAsync(new EventData(Encoding.UTF8.GetBytes(json)));
                    runningMileCount += mileageIncrement;
                    if(runningMileCount>=updateWeatherInverval)
                    {
                        runningMileCount = 0;
                        weatherevents.Clear(); 
                        UpdateWeatherFromTable(new Location(AT.lat, AT.lng), updateWeatherRadius, new TimeSpan(3, 0, 0));
                    }
                }
                catch (Exception exception)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("{0} > Exception: {1}", DateTime.Now.ToString(), exception.Message);
                    Console.ResetColor();
                }


            }
            AT.arrival = DateTime.Now;
            AT.inflight = "false";

            /* fudge the destination position */

            AT.lat=FlightMap.Center.Latitude = destination.Latitude;
            AT.lng=FlightMap.Center.Longitude = destination.Longitude;
            FlightMap.Center.Altitude = AT.altitude;

            UpdateMap(AT, pin, polyline, InfoLayer); 
 
            json = Newtonsoft.Json.JsonConvert.SerializeObject(AT);
            await eventHubClient.SendAsync(new EventData(Encoding.UTF8.GetBytes(json)));
            UpdateFlightInfoBox(AT, true);
            
        }

        private void UpdateMap(AirTelemetry at, Pushpin pin, MapPolyline polyline, MapLayer layer)
        {
            Label lbl = new Label();
            lbl.Content = at.airline + " " + flight;
            FlightMap.Center.Latitude = at.lat;
            FlightMap.Center.Longitude = at.lng; 
            FlightMap.Center.Altitude = at.altitude;
            Location centerd = FlightMap.Center;
            double zoomd = FlightMap.ZoomLevel;
            FlightMap.SetView(centerd, zoomd);
            pin.Location = centerd;
            FlightMap.Children.Clear();
            FlightMap.Children.Add(polyline);
            FlightMap.Children.Add(pin);
            layer.Children.Clear(); 
            layer.AddChild(lbl, centerd);
            WeatherImageLayer(layer);
            FlightMap.Children.Add(layer);
        }
        private MapLayer WeatherImageLayer(MapLayer imageLayer)
        {
            int imageDimension = 60; 
            foreach(WeatherEvent we in weatherevents)
            {
                Image image = new Image();
                image.Height = imageDimension;
                image.Width = imageDimension; 
                //Define the URI location of the image
                BitmapImage myBitmapImage = new BitmapImage();

                myBitmapImage.BeginInit();
                switch(we.eventtype)
                {
                    case WeatherEventTypes.LIGHT_ICE:
                        myBitmapImage.UriSource = new Uri(Directory.GetCurrentDirectory() + @"\ice.png", UriKind.Absolute); 
                        break;
                    case WeatherEventTypes.MODERATE_ICE:
                        myBitmapImage.UriSource = new Uri(Directory.GetCurrentDirectory() + @"\ice.png", UriKind.Absolute); 
                        break;
                    case WeatherEventTypes.HEAVY_ICE:
                        myBitmapImage.UriSource = new Uri(Directory.GetCurrentDirectory() + @"\ice.png", UriKind.Absolute); 
                        break;
                    case WeatherEventTypes.TSTORM:
                        myBitmapImage.UriSource = new Uri(Directory.GetCurrentDirectory() + @"\lightning.png", UriKind.Absolute); 
                        break;
                    case WeatherEventTypes.MODERATE_TURB:
                        myBitmapImage.UriSource = new Uri(Directory.GetCurrentDirectory() + @"\turb.png", UriKind.Absolute); 
                        break;
                    case WeatherEventTypes.HEAVY_TURB:
                        myBitmapImage.UriSource = new Uri(Directory.GetCurrentDirectory() + @"\turb.png", UriKind.Absolute); 
                        break;
                    case WeatherEventTypes.MODERATE_SHEAR:
                        myBitmapImage.UriSource = new Uri(Directory.GetCurrentDirectory() + @"\shear.png", UriKind.Absolute); 
                        break;
                    case WeatherEventTypes.HEAVY_SHEAR:
                        myBitmapImage.UriSource = new Uri(Directory.GetCurrentDirectory() + @"\shear.png", UriKind.Absolute);  
                        break;

                }
                myBitmapImage.DecodePixelHeight = imageDimension;
                myBitmapImage.DecodePixelWidth = imageDimension; 
                myBitmapImage.EndInit();
                image.Source = myBitmapImage;
                image.Opacity = 1.0;
                image.Stretch = System.Windows.Media.Stretch.Fill;

                //Center the image around the location specified
                PositionOrigin position = PositionOrigin.Center;
                //Add the image to the defined map layer
                imageLayer.AddChild(image, we.location, position);
            }
            return imageLayer; 
        }

        bool CreateEventHub()
        {
            try
            {
                var manager = NamespaceManager.CreateFromConnectionString(GetEventHubConnectionString());
                manager.CreateEventHubIfNotExistsAsync(eventHubName).Wait();
                Console.WriteLine("Event Hub is created...");
                return true;
            }
            catch (AggregateException agexp)
            {
                Console.WriteLine(agexp.Flatten());
                return false;
            }
        }
        string GetEventHubConnectionString()
        {
            var connectionString = ConfigurationManager.AppSettings["Microsoft.ServiceBus.ConnectionString"];
            if (string.IsNullOrEmpty(connectionString))
            {
                Console.WriteLine("Did not find Service Bus connections string in appsettings (app.config)");
                return string.Empty;
            }
            try
            {
                var builder = new ServiceBusConnectionStringBuilder(connectionString);
                builder.TransportType = Microsoft.ServiceBus.Messaging.TransportType.Amqp;
                return builder.ToString();
            }
            catch (Exception exception)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(exception.Message);
                Console.ResetColor();
            }

            return null;
        }
        static int GetLatLongFromName(string name, out double latitude, out double longitude)
        {
            string url = "http://dev.virtualearth.net/REST/v1/Locations/";
            string resp;
            string n4 = Uri.EscapeUriString(name); 

            latitude = 0.0;
            longitude = 0.0;

            WebClient wc = new WebClient();
            url += name;
            url += '?' + "o=xml&key=" + bingKey;
            wc.Headers.Add("user-agent", "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; .NET CLR 1.0.3705;)");

            Stream data = wc.OpenRead(url);
            if (data != null)
            {
                StreamReader reader = new StreamReader(data);
                resp = reader.ReadToEnd();
                data.Close();
                reader.Close();

                /* cheesy way to parse this response but whatever */
                string namemkr = "<GeocodePoint><Latitude>";
                int index = resp.IndexOf(namemkr);
                if (index < 0)
                    return -1; 
                index += namemkr.Length;
                int index2 = resp.IndexOf("</Latitude>", index);
                if (index < 0)
                    return -1;
                string latstring = resp.Substring(index, index2 - index);

                namemkr = "<Longitude>";
                index = resp.IndexOf(namemkr, index2);
                if (index < 0)
                    return -1;
                index += namemkr.Length;
                index2 = resp.IndexOf("</Longitude>", index);
                if (index < 0)
                    return -1;
                string longstring = resp.Substring(index, index2 - index);

                latitude = Convert.ToDouble(latstring);
                longitude = Convert.ToDouble(longstring);
                Console.WriteLine("Latitude= " + latitude.ToString() + " Longitude= " + longitude.ToString());
            }
            return 0; 
        }
        /* Bing Map API description: https://msdn.microsoft.com/en-us/library/ff701724.aspx 
         * format: http://dev.virtualearth.net/REST/v1/Imagery/Map/Road/47.619048,-122.35384/15?mapSize=500,500&pp=47.620495,-122.34931;21;AA&pp=47.619385,-122.351485;;AB&pp=47.616295,-122.3556;22&key=BingMapsKey */
        void GetMapFromPosition(Location p)
        {
            string url = "http://dev.virtualearth.net/REST/v1/Imagery/Map/Road/";
            WebClient wc = new WebClient();
            url += p.Latitude.ToString() + ',' + p.Longitude.ToString();
            url += "/1?mapSize=500,500&pp=" + p.Latitude.ToString() + ',' + p.Longitude.ToString();
            url += '&' + "key=" + bingKey;
            wc.Headers.Add("user-agent", "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; .NET CLR 1.0.3705;)");

            BitmapImage src = new BitmapImage();
            src.BeginInit();

            src.UriSource = new Uri(url, UriKind.Absolute);
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.EndInit();
            //FlightCourseImage.Source = src;
        }

 
        private void LtIceClick(object sender, RoutedEventArgs e)
        {
            ltice = true; 
            e.Handled = true; 
        }
        private void ModIceClick(object sender, RoutedEventArgs e)
        {
            mdice = true; 
            e.Handled = true; 
        }
        private void HvyIceClick(object sender, RoutedEventArgs e)
        {
            hvice = true; 
            e.Handled = true; 
        }
        private void ModTurbClick(object sender, RoutedEventArgs e)
        {
            mdtub = true; 
            e.Handled = true; 
        }
        private void HvyTurbClick(object sender, RoutedEventArgs e)
        {
            hvtub = true; 
            e.Handled = true; 
        }
        private void TStrmClick(object sender, RoutedEventArgs e)
        {
            tstrm = true;
            e.Handled = true; 
        }
        private void ModShearClick(object sender, RoutedEventArgs e)
        {
            mdshr = true; 
            e.Handled = true; 
        }
        private void HvyShearClick(object sender, RoutedEventArgs e)
        {
            hvshr = true; 
            e.Handled = true;
        }

        
        #region Azure Table Reading Functions 

        private void UpdateWeatherFromTable(Location pos, double radius, TimeSpan delta)
        {
            if (!tableinitialized)
            {
                var connectionString = ConfigurationManager.AppSettings["Microsoft.Storage.ConnectionString"];
                if (string.IsNullOrEmpty(connectionString))
                {
                    Console.WriteLine("Did not find table storage connection string in appsettings (app.config)");
                    return;
                }

                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
                // Create service client for credentialed access to the Table service.
                tableClient = new CloudTableClient(storageAccount.TableEndpoint, storageAccount.Credentials);
                table = tableClient.GetTableReference("airtable");
                tableinitialized = true; 
            }
            List<string> results = new List<string>();
            DateTime now = DateTime.Now;
            DateTime since = now.Subtract(delta);
            DateTimeOffset dto = since;

            string partitionFilter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, "airtelemetry");
            string sinceFilter = TableQuery.GenerateFilterConditionForDate("dto", QueryComparisons.GreaterThan, since); 

            string finalFilter = TableQuery.CombineFilters(partitionFilter, TableOperators.And, sinceFilter);

            TableQuery<AirTelemetry> query = new TableQuery<AirTelemetry>().Where(finalFilter);

            /* clearly not the most efficient way to do this but ok for a POC */ 
            foreach(AirTelemetry atrec in table.ExecuteQuery(query))
            {
                Console.WriteLine(atrec.airline +" " + atrec.flight);
                if(Haversine.InRange(pos, atrec.lat,atrec.lng, radius))
                {
                    if(atrec.lightning>20.0)
                    {
                        WeatherEvent we = new WeatherEvent(WeatherEventTypes.TSTORM, atrec.dto, new Location(atrec.lat, atrec.lng));
                        weatherevents.Add(we);
                    }
                    if (atrec.ice == 5.0)
                    {
                        WeatherEvent we = new WeatherEvent(WeatherEventTypes.LIGHT_ICE, atrec.dto, new Location(atrec.lat, atrec.lng));
                        weatherevents.Add(we);
                    }
                    if (atrec.ice == 10.0)
                    {
                        WeatherEvent we = new WeatherEvent(WeatherEventTypes.MODERATE_ICE, atrec.dto, new Location(atrec.lat, atrec.lng));
                        weatherevents.Add(we);
                    }
                    if (atrec.ice > 10.0)
                    {
                        WeatherEvent we = new WeatherEvent(WeatherEventTypes.HEAVY_ICE, atrec.dto, new Location(atrec.lat, atrec.lng));
                        weatherevents.Add(we);
                    }
                    if(atrec.windspeed==20.0 )
                    {
                        WeatherEvent we = new WeatherEvent(WeatherEventTypes.MODERATE_TURB, atrec.dto, new Location(atrec.lat, atrec.lng));
                        weatherevents.Add(we);
                    }
                    if (atrec.windspeed > 20.0)
                    {
                        WeatherEvent we = new WeatherEvent(WeatherEventTypes.HEAVY_TURB, atrec.dto, new Location(atrec.lat, atrec.lng));
                        weatherevents.Add(we);
                    }
                    if(atrec.windshear>=15.0 && atrec.windshear<=25.0)
                    {
                        WeatherEvent we = new WeatherEvent(WeatherEventTypes.MODERATE_SHEAR, atrec.dto, new Location(atrec.lat, atrec.lng));
                        weatherevents.Add(we);
                    }
                    if (atrec.windshear > 20.0)
                    {
                        WeatherEvent we = new WeatherEvent(WeatherEventTypes.HEAVY_SHEAR, atrec.Timestamp, new Location(atrec.lat, atrec.lng));
                        weatherevents.Add(we);
                    }
                }
            }

            return; 
        }

        #endregion

        /* this is a handy event handler to use for debug code */ 
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            return; 
        }
    }
}
