
using Microsoft.Identity.Client;
using System;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.Serialization;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ColiboAs
{
    class Program
    {
        static List<User> graphUsers = new();
        static List<Person> xmlUsers = new();
        static async Task Main(string[] args)
        {


            //Get users from xml file
            GetXMLPersonList();
            //Get users from Graph or fake json file
            await GetGraphUsers();
            //Merge users into the database(json file)
            MergeUsers();
            AddUserToColiboXML();
            //UpdateUserInColiboXML();
        }
        static void AddUserToColiboXML()
        {
            var serializer = new XmlSerializer(typeof(XmlPersonList));
            XmlPersonList userList;
            using (var stream = new FileStream("ColiboXML.xml", FileMode.Open))
            {
                userList = serializer.Deserialize(stream) as XmlPersonList ?? new XmlPersonList();
            }
            if (!userList.PersonList.Any(x => x.Name == "Curcuta Lucian"))
            {
                // Add a new person, if user doesnt exists
                var newPerson = new Person
                {
                    Number = 1059,
                    Name = "Curcuta Lucian",
                    Email = "luciancosntC@gmail.com",
                    Mobile = "+45 52720543",
                    Title = "Softwareudvikler",
                    Address = "Mollehatten20",
                    City = "Aarhus"
                };
                userList.PersonList.Add(newPerson);
            }

            using (FileStream stream = new FileStream("ColiboXML.xml", FileMode.Create))
            {
                serializer.Serialize(stream, userList);
            }

        }
        static void UpdateUserInColiboXML()
        {
            var serializer = new XmlSerializer(typeof(XmlPersonList));
            XmlPersonList userList;
            using (var stream = new FileStream("ColiboXML.xml", FileMode.Open))
            {
                userList = serializer.Deserialize(stream) as XmlPersonList ?? new XmlPersonList();
            }
            // Update first user
            var currentColiboUser = userList.PersonList.FirstOrDefault();
            currentColiboUser.Title = "CTO";

            using (FileStream stream = new FileStream("ColiboXML.xml", FileMode.Create))
            {
                serializer.Serialize(stream, userList);
            }

        }

        static ColiboStorageUser GetColiboUsers()
        {
            string filePath = "ColiboUsers.json";
            string json = File.ReadAllText(filePath);
            if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
            {
                var graphData = JsonSerializer.Deserialize<ColiboStorageUser>(json);
                return graphData ?? new ColiboStorageUser { Users = new List<ColiboUsers>() }; ;
                ;
            }
            else
            {
                return new ColiboStorageUser { Users = new List<ColiboUsers>() };
            }

        }
        static async Task GetGraphUsers()
        {
            ////Optional: User fake json instead 
            //string json = File.ReadAllText("GraphUsers.json");
            //var graphData = JsonSerializer.Deserialize<GraphUsers>(json);
            //graphUsers = graphData?.Users ?? [];

            DotNetEnv.Env.Load();
            var clientId = Environment.GetEnvironmentVariable("Graph_ClientID");
            var tenantId = Environment.GetEnvironmentVariable("Graph_TenantId");
            var clientSecret = Environment.GetEnvironmentVariable("Graph_ClientSecret");
            var app = ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority(new Uri($"https://login.microsoftonline.com/{tenantId}")).Build();


            try
            {
                var tokenResponse = await app.AcquireTokenForClient(new[] { ".default" }).ExecuteAsync();

                Console.WriteLine("Token acquired successfully.");
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization =
           new AuthenticationHeaderValue("Bearer", tokenResponse.AccessToken);

                    var request = new HttpRequestMessage();
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                    tokenResponse.AccessToken);
                    var requestUrl = "https://graph.microsoft.com/v1.0/users"; // You can change to /me or any other endpoint
                    var response = await client.GetAsync(requestUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();

                        var graphUsersX = JsonSerializer.Deserialize<GraphUsers>(content);
                        graphUsers = graphUsersX.Users;

                        Console.WriteLine("MS Graph API Response:");
                        Console.WriteLine(content);
                    }
                    else
                    {
                        Console.WriteLine($"Request failed with status {response.StatusCode}");
                        var error = await response.Content.ReadAsStringAsync();
                        Console.WriteLine(error);
                    }
                }
            }

            catch (MsalServiceException ex)
            {
                Console.WriteLine($"MSAL error: {ex.Message}");

            }
        }
        static void AddColiboUser()
        {
            var coliboUsers = GetColiboUsers();
            //Find unique users, which dont exists in both lists
            var uniqueUsers = xmlUsers
        .Where(person => !graphUsers.Any(user =>
            user.EmployeeId == person.Number.ToString() ||
            user.DisplayName == person.Name))
           .Select(p => new { Name = p.Name, Source = "XML" })
        .Concat(
            graphUsers
                .Where(user => !xmlUsers.Any(person =>
                    user.EmployeeId == person.Number.ToString() ||
                    user.DisplayName == person.Name))
      .Select(u => new { Name = u.DisplayName, Source = "Graph" }))
        .ToList();

            // make sure they dont exist in the ColiboDatabase
            var filteredNewUsers = uniqueUsers.Where(x => !coliboUsers.Users.Any(z => z.FullName == x.Name))
    .ToList();
            var userList = new List<ColiboUsers>();
            foreach (var user in filteredNewUsers)
            {

                object? userInfo = user.Source == "Graph" ? graphUsers.FirstOrDefault(x => x.DisplayName == user.Name) : xmlUsers.FirstOrDefault(x => x.Name == user.Name);
                //prepare Colibo entry info
                if (userInfo != null)
                {
                    var newUser = new ColiboUsers
                    {
                        BusinessPhones = user.Source == "Graph" ? (userInfo as User)?.BusinessPhones ?? null : null,
                        FullName = user.Source == "Graph" ? (userInfo as User)?.DisplayName ?? null : (userInfo as Person)?.Name ?? null,
                        JobTitle = user.Source == "Graph" ? (userInfo as User)?.JobTitle ?? null : (userInfo as Person)?.Title ?? null,
                        Mail = user.Source == "Graph" ? [(userInfo as User)?.Mail ?? null] : [(userInfo as Person)?.Email ?? null],
                        MobilePhone = user.Source == "Graph" ? (userInfo as User)?.MobilePhone ?? null : (userInfo as Person)?.Mobile ?? null,
                        OfficeLocation = user.Source == "Graph" ? (userInfo as User)?.OfficeLocation ?? null : null,
                        PreferredLanguage = user.Source == "Graph" ? (userInfo as User)?.PreferredLanguage ?? null : "",
                        Email = user.Source == "Graph" ? (userInfo as User)?.UserPrincipalName ?? null : (userInfo as Person)?.Email ?? null,
                        Id = user.Source == "Graph" ? (userInfo as User)?.Id ?? null : (userInfo as Person)?.Number.ToString() ?? null,
                        City = user.Source == "Graph" ? null : (userInfo as Person)?.City ?? null,
                        Address = user.Source == "Graph" ? null : (userInfo as Person)?.Address ?? null,

                    };
                    userList.Add(newUser);
                }
            }
            ColiboStorageUser storage;

            storage = GetColiboUsers();
            storage.Users ??= new List<ColiboUsers>();
            //add users to colibo database
            foreach (var user in userList)
            {
                storage.Users.Add(user);
            }


            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            string updatedJson = JsonSerializer.Serialize(storage, jsonOptions);
            File.WriteAllText("ColiboUsers.json", updatedJson);
        }
        static void UpdateColiboUsers()
        {

            //Get database users 
            ColiboStorageUser storage = GetColiboUsers();
            //Initialize if db empty
            if (storage.Users == null)
                storage.Users = new List<ColiboUsers>();
            //Match to common users based on ID or Name 
            var matchedFromXml = xmlUsers
            .Where(person => graphUsers.Any(user =>
            user.EmployeeId == person.Number.ToString() ||
            user.DisplayName == person.Name)).ToList();

            foreach (var xmlUser in matchedFromXml)
            {
                var currentColiboUser = storage.Users.FirstOrDefault(x => x.FullName == xmlUser.Name);
                var currentGraphUser = graphUsers.FirstOrDefault(x => x.DisplayName == xmlUser.Name);

                if (currentColiboUser != null && currentGraphUser != null)
                {
                    //Update user in database
                    currentColiboUser.BusinessPhones = currentGraphUser.BusinessPhones ?? currentGraphUser.BusinessPhones;
                    currentColiboUser.FullName = currentGraphUser.DisplayName ?? xmlUser.Name ?? currentColiboUser.FullName;
                    currentGraphUser.JobTitle = currentGraphUser.JobTitle ?? xmlUser.Title ?? currentColiboUser.JobTitle;
                    currentColiboUser.Mail = currentGraphUser.Mail != xmlUser.Email && xmlUser.Email != null && currentGraphUser.Mail != null ? [currentGraphUser.Mail, xmlUser.Email] : currentColiboUser.Mail;
                    currentColiboUser.MobilePhone = currentGraphUser.MobilePhone ?? xmlUser.Mobile ?? currentColiboUser.MobilePhone;
                    currentColiboUser.OfficeLocation = currentGraphUser.OfficeLocation ?? currentColiboUser.OfficeLocation;
                    currentColiboUser.PreferredLanguage = currentGraphUser.PreferredLanguage ?? currentColiboUser.PreferredLanguage;
                    currentColiboUser.Email = currentGraphUser.UserPrincipalName ?? xmlUser.Email ?? currentColiboUser.Email;
                    currentColiboUser.Id = currentGraphUser.Id ?? xmlUser.Number.ToString() ?? currentColiboUser.Id;
                    currentColiboUser.City = xmlUser.City ?? currentColiboUser.City;
                    currentColiboUser.Address = xmlUser.Address ?? currentColiboUser.Address;
                }
                else
                {
                    //User doesnt exist, so add them user to database
                    var newUser = new ColiboUsers
                    {
                        BusinessPhones = currentGraphUser.BusinessPhones ?? [],
                        FullName = currentGraphUser.DisplayName ?? xmlUser.Name ?? currentColiboUser.FullName,
                        JobTitle = currentGraphUser.JobTitle ?? xmlUser.Title ?? "",
                        Mail = currentGraphUser.Mail != xmlUser.Email && xmlUser.Email != null && currentGraphUser.Mail != null ? [currentGraphUser.Mail, xmlUser.Email] : [],
                        MobilePhone = currentGraphUser.MobilePhone ?? xmlUser.Mobile ?? "",
                        OfficeLocation = currentGraphUser.OfficeLocation ?? "",
                        PreferredLanguage = currentGraphUser.PreferredLanguage ?? "",
                        Email = currentGraphUser.UserPrincipalName ?? xmlUser.Email ?? "",
                        Id = currentGraphUser.Id ?? xmlUser.Number.ToString() ?? "",
                        City = xmlUser.City ?? "",
                        Address = xmlUser.Address ?? "",

                    };
                    storage.Users.Add(newUser);
                }
                var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
                string updatedJson = JsonSerializer.Serialize(storage, jsonOptions);
                File.WriteAllText("ColiboUsers.json", updatedJson);
            }
        }
        static void GetXMLPersonList()
        {
            using var stream = new FileStream("ColiboInternalUsers.xml", FileMode.Open);
            var serializer = new XmlSerializer(typeof(XmlPersonList));
            var userList = serializer.Deserialize(stream) as XmlPersonList;
            xmlUsers = userList?.PersonList ?? [];
        }

        static void MergeUsers()
        {
            UpdateColiboUsers();
            AddColiboUser();

        }
    }

}
[XmlRoot("data")]
public class XmlPersonList
{
    [XmlArray("persons")]
    [XmlArrayItem("person")]
    public List<Person>? PersonList { get; set; }
}

public class Person
{
    [XmlAttribute("number")]
    public int Number { get; set; }

    [XmlElement("name")]
    public string? Name { get; set; }

    [XmlElement("email")]
    public string? Email { get; set; }

    [XmlElement("mobile")]
    public string? Mobile { get; set; }

    [XmlElement("title")]
    public string? Title { get; set; }

    [XmlElement("address")]
    public string? Address { get; set; }

    [XmlElement("city")]
    public string? City { get; set; }



}

public class GraphUsers
{
    [JsonPropertyName("value")]
    public List<User>? Users { get; set; }
}

public class User
{
    [JsonPropertyName("businessPhones")]
    public List<string>? BusinessPhones { get; set; }
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; set; }
    [JsonPropertyName("givenName")]
    public string? GivenName { get; set; }
    [JsonPropertyName("jobTitle")]
    public string? JobTitle { get; set; }
    [JsonPropertyName("mail")]
    public string? Mail { get; set; }
    [JsonPropertyName("mobilePhone")]
    public string? MobilePhone { get; set; }
    [JsonPropertyName("officeLocation")]
    public string? OfficeLocation { get; set; }
    [JsonPropertyName("preferredLanguage")]
    public string? PreferredLanguage { get; set; }
    [JsonPropertyName("surname")]
    public string? Surname { get; set; }
    [JsonPropertyName("userPrincipalName")]
    public string? UserPrincipalName { get; set; }
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    [JsonPropertyName("employeeId")]
    public string? EmployeeId { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }

}
public class ColiboUsers
{
    public List<string> BusinessPhones { get; set; } = new();
    public string? FullName { get; set; }
    public string? JobTitle { get; set; }
    public List<string> Mail { get; set; }
    public string? MobilePhone { get; set; }
    public string? OfficeLocation { get; set; }
    public string? PreferredLanguage { get; set; }
    public string? Email { get; set; }
    public string? Id { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }

}

public class ColiboStorageUser
{
    public List<ColiboUsers>? Users { get; set; }
}
