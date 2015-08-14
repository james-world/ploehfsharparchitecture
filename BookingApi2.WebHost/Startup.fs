namespace BookingApi2.WebHost

open Owin
open Microsoft.Owin
open System
open System.Net.Http
open System.Web
open System.Web.Http
open System.Web.Http.Owin
open BookingApi2
open BookingApi2.Infrastructure
open System.Collections.Concurrent

[<Sealed>]
type Startup() =

    static member RegisterWebApi(config: HttpConfiguration) =
        // Configure routing
        config.MapHttpAttributeRoutes()

        // Configure serialization
        config.Formatters.XmlFormatter.UseXmlSerializer <- true
        config.Formatters.JsonFormatter.SerializerSettings.ContractResolver <- Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()

        // Additional Web API settings

    member __.Configuration(builder: IAppBuilder) =
        let config = new HttpConfiguration()
        Configure (ConcurrentBag<Envelope<Reservation>>()) config
        Startup.RegisterWebApi config
        builder.UseWebApi config |> ignore

