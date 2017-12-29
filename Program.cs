using System;
using System.Threading;
using Unosquare.RaspberryIO;
using Unosquare.RaspberryIO.Gpio;

namespace Com.Bekijkhet.MyTherm
{
    class Program
    {
        static void Main(string[] args)
        {
            try {
                var dht = new DHT(Pi.Gpio.Pin07, DHTSensorTypes.DHT22);
                while (true) {
                    try {
                        var d = dht.ReadData();
                        Console.WriteLine(DateTime.UtcNow);
                        Console.WriteLine(" temp: " + d.TempCelcius);
                        Console.WriteLine(" hum: " + d.Humidity);
                    } catch (DHTException) {
                    }
                    Thread.Sleep(10000);
                }
            }
            catch (Exception e) {
                Console.Error.WriteLine(e.Message + " - " + e.StackTrace);
            }
        }
    }
}