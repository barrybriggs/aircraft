### Aircraft Telemetry Simulation ###

By Barry Briggs, Microsoft
(c) 2015
MIT License

**Aircraft** is a WPF application simulating the real-time reporting of weather conditions along a flight path. It takes as input an airline/flight (not validated in any way) and origin and destination airport codes, for example, IAD and BOS for Washington Dulles and Boston, MA respectively. As the plane "flies" you can hit buttons to report various weather encountered in the flight, such as icing, turbulence and wind shear. Subsequent "flights" flying nearby will see these previously recorded events up to three hours later. 

The data is sent in JSON format to an Azure Event Hub. I use Azure Stream Analytics to take the events and store them in an Azure Table. All flights periodically (every 100 miles or so) look in the Azure Table to see if there are any previously reported weather events that might affect them. 

This application includes a static C# implementation of the `Haversine algorithm` which calculates distance and bearings based on latitudes, longitudes and the radius of the earth. This part of the code is implemented as a static class and is easily reused; functions include:



- `double HaversineDistance(Location loc1, Location loc2)` : returns distance in meters between two points
- `double Bearing (Location loc1, Location loc2)` : returns the bearing in degrees between two points 
- `bool InRange(Location loc1, double latitude, double longitude, double distance)` : is a given point within 'distance' meters of another point  


There are a number of good references on the Haversine algorithm, including [Wikipedia](https://en.wikipedia.org/wiki/Haversine_formula) and this excellent [article](http://www.movable-type.co.uk/scripts/latlong.html) which provides a JavaScript implementation. 

Note that you should be continuously recalculating the Bearing as you fly long distances in order to get an efficient [Great Circle](https://en.wikipedia.org/wiki/Great_circle) route! 

####Prerequisites####

To develop and run this program you will need: 

- Visual Studio (2013 or greater)
- An Azure subscription
- An Azure Service Bus connection string (stored in app.config)
- An Azure Storage Table with a storage key and connection string (stored in app.config)
- Azure SDK (I used v2.4)
- Bing Maps WPF control 
- Bing Maps key (development key is free; go [here](http://www.microsoft.com/maps/choose-your-bing-maps-API.aspx)) 


####Status####
This is a fairly primitive proof of concept. Using Azure SQL or Azure DocumentDB would probably improve queries and lookup time (the scan of weather events is done entirely by the application).    
