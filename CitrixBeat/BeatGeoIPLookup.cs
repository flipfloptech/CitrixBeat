using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CitrixBeat
{
    class BeatGeoIPLookup
    {

        List<BeatGeoIP> _internalDB = new List<BeatGeoIP>();
        public BeatGeoIP QueryGeographicalLocation(string ipAddress)
        {
            //internal db check
            try
            {
                BeatGeoIP _search = _internalDB.First(_geoip => _geoip.IP == ipAddress);
                return _search;
            }
            catch
            { 
                try
                {
                    HttpClient client = new HttpClient();
                    string result = client.GetStringAsync("http://freegeoip.net/json/" + ipAddress).Result;
                    BeatGeoIP _geoip = JsonConvert.DeserializeObject<BeatGeoIP>(result);
                    _internalDB.Add(_geoip);
                    return _geoip;
                }
                catch
                { return null; }
            }
        }
        public class BeatGeoIP
        {
            [JsonProperty("ip")]
            public string IP { get; set; }

            [JsonProperty("country_code")]

            public string CountryCode { get; set; }

            [JsonProperty("country_name")]

            public string CountryName { get; set; }

            [JsonProperty("region_code")]

            public string RegionCode { get; set; }

            [JsonProperty("region_name")]

            public string RegionName { get; set; }

            [JsonProperty("city")]

            public string City { get; set; }

            [JsonProperty("zip_code")]

            public string ZipCode { get; set; }

            [JsonProperty("time_zone")]

            public string TimeZone { get; set; }

            [JsonProperty("latitude")]

            public float Latitude { get; set; }

            [JsonProperty("longitude")]

            public float Longitude { get; set; }

            [JsonProperty("metro_code")]

            public int MetroCode { get; set; }
        }
    }
    
}
