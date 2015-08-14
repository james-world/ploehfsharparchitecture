namespace BookingApi2.WebHost

open Owin
open Microsoft.Owin
open System
open System.Reactive
open System.Net.Http
open System.Web
open System.Web.Http
open System.Web.Http.Owin
open BookingApi2
open BookingApi2.Infrastructure
open BookingApi2.Reservations
open System.Collections.Concurrent
open System.Reactive.Subjects


type Agent<'a> = Microsoft.FSharp.Control.MailboxProcessor<'a>

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
        let seatingCapacity = 10
        let config = new HttpConfiguration()
        let reservations = ConcurrentBag<Envelope<Reservation>>()
        let notifications = ConcurrentBag<Envelope<Notification>>()

        let reservationSubject = new Subject<Envelope<Reservation>>()
        reservationSubject.Subscribe reservations.Add |> ignore

        let notificationSubject = new Subject<Notification>()
        notificationSubject
        |> Observable.map EnvelopWithDefaults
        |> Observable.subscribe notifications.Add
        |> ignore

        let agent = new Agent<Envelope<MakeReservation>>(fun inbox ->
            let rec loop () =
                async {
                    let! cmd = inbox.Receive()
                    let rs = reservations |> ToReservations
                    let handle = Handle seatingCapacity rs
                    let newReservations = handle cmd
                    match newReservations with 
                    | Some(r) ->
                        reservationSubject.OnNext r
                        notificationSubject.OnNext
                            {
                                About = cmd.Id
                                Type = "Success"
                                Message =
                                    sprintf
                                        "Your reservation for %s was completed. We look forward to seeing you."
                                        (cmd.Item.Date.ToString "yyyy.MM.dd")
                            }
                    | _ ->
                        notificationSubject.OnNext
                            {
                                About = cmd.Id
                                Type = "Failure"
                                Message =
                                    sprintf
                                        "We regret to inform you that your reservation for %s could not be completed, because we are already fully booked."
                                        (cmd.Item.Date.ToString "yyyy.MM.dd")
                            }
                    return! loop() }
            loop())
        do agent.Start()

        Configure
            (reservations |> ToReservations)
            (Observer.Create (fun x-> agent.Post x))
            config
        
        Startup.RegisterWebApi config
        builder.UseWebApi config |> ignore

