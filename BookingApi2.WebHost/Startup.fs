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
open System.IO
open Newtonsoft.Json


type ReservationsInFiles(directory : DirectoryInfo) =
    let toReservation (f: FileInfo) =
        let json = File.ReadAllText f.FullName
        JsonConvert.DeserializeObject<Envelope<Reservation>>(json)
    let toEnumerator (s : seq<'a>) = s.GetEnumerator()
    let getContainingDirectory (d : DateTime) =
        Path.Combine(
            directory.FullName,
            d.Year.ToString(),
            d.Month.ToString(),
            d.Day.ToString())
    let appendPath p2 p1 = Path.Combine(p1, p2)
    let getJsonFiles (dir : DirectoryInfo) =
        if Directory.Exists(dir.FullName) then
            dir.EnumerateFiles("*.json", SearchOption.AllDirectories)
        else
            Seq.empty<FileInfo>

    member this.Write (reservation : Envelope<Reservation>) =
        let withExtension extension path = Path.ChangeExtension(path, extension)
        let directoryName = reservation.Item.Date |> getContainingDirectory
        let fileName =
            directoryName
            |> appendPath (reservation.Id.ToString())
            |> withExtension "json"
        let json = JsonConvert.SerializeObject reservation
        Directory.CreateDirectory directoryName |> ignore
        File.WriteAllText(fileName, json)

    interface IReservations with
        member this.Between min max =
            Dates.InitInfinite min
            |> Seq.takeWhile (fun d-> d <= max)
            |> Seq.map getContainingDirectory
            |> Seq.collect (fun dir -> DirectoryInfo(dir) |> getJsonFiles)
            |> Seq.map toReservation

        member this.GetEnumerator() =
            directory
            |> getJsonFiles
            |> Seq.map toReservation
            |> toEnumerator

        member this.GetEnumerator() =
            (this :> seq<Envelope<Reservation>>).GetEnumerator()
                :> System.Collections.IEnumerator

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

        let dir = DirectoryInfo(System.Web.HttpContext.Current.Server.MapPath("~/ReservationStore"))
        let reservations = ReservationsInFiles(dir)
        let notifications = ConcurrentBag<Envelope<Notification>>()

        let reservationSubject = new Subject<Envelope<Reservation>>()
        reservationSubject.Subscribe reservations.Write |> ignore

        let notificationSubject = new Subject<Notification>()
        notificationSubject
        |> Observable.map EnvelopWithDefaults
        |> Observable.subscribe notifications.Add
        |> ignore

        let agent = new Agent<Envelope<MakeReservation>>(fun inbox ->
            let rec loop () =
                async {
                    let! cmd = inbox.Receive()
                    let handle = Handle seatingCapacity reservations
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
            reservations
            (Observer.Create (fun x-> agent.Post x))
            (notifications |> Notifications.ToNotifications)
            seatingCapacity
            config
        
        Startup.RegisterWebApi config
        builder.UseWebApi config |> ignore

