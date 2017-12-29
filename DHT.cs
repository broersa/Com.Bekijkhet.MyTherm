using System;
using System.Threading;
using System.Threading.Tasks;
using Unosquare.RaspberryIO;
using Unosquare.RaspberryIO.Gpio;

namespace Com.Bekijkhet.MyTherm
{
    public class DHT
    {
        private UInt32[] _data;

        private GpioPin _dataPin;

        private bool _firstReading;

        private DateTime _prevReading;

        private DHTSensorTypes _sensorType;

        public DHT(GpioPin datatPin, DHTSensorTypes sensor)
        {
            if (datatPin != null)
            {
                _dataPin = datatPin;
                _firstReading = true;
                _prevReading = DateTime.MinValue;
                _data = new UInt32[6];
                _sensorType = sensor;
                //Init the data pin
                _dataPin.PinMode = GpioPinDriveMode.Output;
                _dataPin.Write(GpioPinValue.High);
            }
            else
            {
                throw new ArgumentException("Parameter cannot be null.", "dataPin");
            }
        }

        public DHTData ReadData()
        {
            float t = 0;
            float h = 0;

            if (Read())
            {
                switch (_sensorType)
                {
                    case DHTSensorTypes.DHT11:
                        t = _data[2];
                        h = _data[0];
                        break;
                    case DHTSensorTypes.DHT22:
                    case DHTSensorTypes.DHT21:
                        t = _data[2] & 0x7F;
                        t *= 256;
                        t += _data[3];
                        t /= 10;
                        if ((_data[2] & 0x80) != 0)
                        {
                            t *= -1;
                        }
                        h = _data[0];
                        h *= 256;
                        h += _data[1];
                        h /= 10;
                        break;
                }
                return new DHTData() {
                    TempCelcius = t,
                    TempFahrenheit = ConvertCtoF(t),
                    Humidity = h,
                    HeatIndex = ComputeHeatIndex(t, h, false)       
                };
            }
            throw new DHTException();
        }

        float ConvertCtoF(float c)
        {
            return c * 9 / 5 + 32;
        }

        float ConvertFtoC(float f)
        {
            return (f - 32) * 5 / 9;
        }

        private double ComputeHeatIndex(float temperature, float percentHumidity, bool isFahrenheit)
        {
            // Adapted from equation at: https://github.com/adafruit/DHT-sensor-library/issues/9 and
            // Wikipedia: http://en.wikipedia.org/wiki/Heat_index
            if (!isFahrenheit)
            {
                // Celsius heat index calculation.
                return -8.784695 +
                            1.61139411 * temperature +
                            2.338549 * percentHumidity +
                        -0.14611605 * temperature * percentHumidity +
                        -0.01230809 * Math.Pow(temperature, 2) +
                        -0.01642482 * Math.Pow(percentHumidity, 2) +
                            0.00221173 * Math.Pow(temperature, 2) * percentHumidity +
                            0.00072546 * temperature * Math.Pow(percentHumidity, 2) +
                        -0.00000358 * Math.Pow(temperature, 2) * Math.Pow(percentHumidity, 2);
            }
            else
            {
                // Fahrenheit heat index calculation.
                return -42.379 +
                            2.04901523 * temperature +
                        10.14333127 * percentHumidity +
                        -0.22475541 * temperature * percentHumidity +
                        -0.00683783 * Math.Pow(temperature, 2) +
                        -0.05481717 * Math.Pow(percentHumidity, 2) +
                            0.00122874 * Math.Pow(temperature, 2) * percentHumidity +
                            0.00085282 * temperature * Math.Pow(percentHumidity, 2) +
                        -0.00000199 * Math.Pow(temperature, 2) * Math.Pow(percentHumidity, 2);
            }
        }

        private bool Read()
        {
            var now = DateTime.UtcNow;

            if (!_firstReading && ((now - _prevReading).TotalMilliseconds < 2000))
            {
                return false;
            }

            _firstReading = false;
            _prevReading = now;;

            _data[0] = _data[1] = _data[2] = _data[3] = _data[4] = 0;

            _dataPin.PinMode=GpioPinDriveMode.Output;

            _dataPin.Write(GpioPinValue.High);

            Thread.Sleep(250);

            _dataPin.Write(GpioPinValue.Low);

            Thread.Sleep(20);

            //TIME CRITICAL ###############
            _dataPin.Write(GpioPinValue.High);
            //=> DELAY OF 40 microseconds needed here
            WaitMicroseconds(40);

            _dataPin.PinMode=GpioPinDriveMode.Input;
            //Delay of 10 microseconds needed here
            WaitMicroseconds(10);

            if (ExpectPulse(GpioPinValue.Low) == 0)
            {
                return false;
            }
            if (ExpectPulse(GpioPinValue.High) == 0)
            {
                return false;
            }

            // Now read the 40 bits sent by the sensor.  Each bit is sent as a 50
            // microsecond low pulse followed by a variable length high pulse.  If the
            // high pulse is ~28 microseconds then it's a 0 and if it's ~70 microseconds
            // then it's a 1.  We measure the cycle count of the initial 50us low pulse
            // and use that to compare to the cycle count of the high pulse to determine
            // if the bit is a 0 (high state cycle count < low state cycle count), or a
            // 1 (high state cycle count > low state cycle count).
            for (int i = 0; i < 40; ++i)
            {
                UInt32 lowCycles = ExpectPulse(GpioPinValue.Low);
                if (lowCycles == 0)
                {
                    return false;
                }
                UInt32 highCycles = ExpectPulse(GpioPinValue.High);
                if (highCycles == 0)
                {
                    return false;
                }
                _data[i / 8] <<= 1;
                // Now compare the low and high cycle times to see if the bit is a 0 or 1.
                if (highCycles > lowCycles)
                {
                    // High cycles are greater than 50us low cycle count, must be a 1.
                    _data[i / 8] |= 1;
                }
                // Else high cycles are less than (or equal to, a weird case) the 50us low
                // cycle count so this must be a zero.  Nothing needs to be changed in the
                // stored data.
            }
            //TIME CRITICAL_END #############

            // Check we read 40 bits and that the checksum matches.
            if (_data[4] == ((_data[0] + _data[1] + _data[2] + _data[3]) & 0xFF))
            {
                return true;
            }
            else
            {
                //Checksum failure!
                return false;
            }
        }

        private UInt32 ExpectPulse(GpioPinValue level)
        {
            UInt32 count = 0;

            while (_dataPin.Read() == (level == GpioPinValue.High))
            {
                count++;
                //WaitMicroseconds(1);
                if (count == 10000)
                {
                    return 0;
                }
            }
            return count;
        }

        private void WaitMicroseconds(int microseconds)
        {
            var until = DateTime.UtcNow.Ticks + (microseconds*10);
            while (DateTime.UtcNow.Ticks < until) {}
        }
    }
}