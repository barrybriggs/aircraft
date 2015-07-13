///
/// Static class for Haversine algorithms in C#.
/// 
/// This class uses the Microsoft.Maps.Mapcontrol DLL which is part of the Bing Maps API. In this class it is
/// only used to define the Location class holding Latitude and Longitude. If you do not want to use the Bing Maps
/// SDK you can simply define a class or a struct like this:
/// 
/// public class Location {
///     public double Latitude;
///     public double Longitude;
/// }
/// 
/// There are a number of good references on the Havesine algorithm:
/// 
///   https://en.wikipedia.org/wiki/Haversine_formula  :    basic background
///   http://www.movable-type.co.uk/scripts/latlong.html :  JavaScript implementation
///   
///

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maps.MapControl.WPF;

namespace Plane
{
    static class Haversine
    {
        /* algorithms adapted from http://www.movable-type.co.uk/scripts/latlong.html */
        public static double HaversineDistance(Location p1, Location p2)
        {
            double radius = 6371000;
            double phi1 = p1.Latitude * (Math.PI / 180.0);
            double phi2 = p2.Latitude * (Math.PI / 180.0);
            double deltaphi = (p2.Latitude - p1.Latitude) * (Math.PI / 180.0);
            double deltalon = (p2.Longitude - p1.Longitude) * (Math.PI / 180.0);

            double a = Math.Sin(deltaphi / 2) * Math.Sin(deltaphi / 2) + Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltalon / 2) * Math.Sin(deltalon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            double distance = radius * c;
            return distance;
        }
        public static double Bearing(Location p1, Location p2)
        {
            double phi1 = p1.Latitude * (Math.PI / 180.0);
            double phi2 = p2.Latitude * (Math.PI / 180.0);
            double deltaphi = (p2.Latitude - p1.Latitude) * (Math.PI / 180.0);
            double deltalon = (p2.Longitude - p1.Longitude) * (Math.PI / 180.0);
            double y = Math.Sin(deltalon) * Math.Cos(phi2);
            double x = Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(deltalon);
            double bearing = Math.Atan2(y, x) * (180.0 / Math.PI);
            return bearing;
        }
        /* given initial position, distance and bearing, return new position; note all in radians */
        public static Location PositionDistanceFromOrigin(Location p1, double distance, double bearing)
        {
            double radius = 6371000;
            Location p = new Location(0, 0);
            double phi1 = p1.Latitude * (Math.PI / 180.0);
            double lng1 = p1.Longitude * (Math.PI / 180.0);
            double brad = bearing * (Math.PI / 180.0);

            double lat = Math.Asin(Math.Sin(phi1) * Math.Cos(distance / radius) + Math.Cos(phi1) * Math.Sin(distance / radius) * Math.Cos(brad));
            double lng = lng1 + Math.Atan2(Math.Sin(brad) * Math.Sin(distance / radius) * Math.Cos(p.Latitude), Math.Cos(distance / radius) - Math.Sin(phi1) * Math.Sin(lat));

            p.Latitude = lat * (180.0 / Math.PI);
            p.Longitude = lng * (180.0 / Math.PI);

            return p;
        }
        public static bool InRange(Location p1, double lat, double lng, double distance)
        {
            Location p2 = new Location(lat, lng);
            double d = HaversineDistance(p1, p2);
            double miles = d * 0.00062137;
            if (miles < distance)
                return true;
            else
                return false;

        }
    }
}
