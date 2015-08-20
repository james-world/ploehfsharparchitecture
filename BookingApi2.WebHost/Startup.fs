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
open Microsoft.WindowsAzure.Storage.Blob
open Newtonsoft.Json
open Microsoft.WindowsAzure.Storage
open Microsoft.Azure

[<CLIMutable>]
type StoredReservations = {
    Reservations : Envelope<Reservation> array
    AcceptedCommandIds : Guid array }

type ReservationsInAzureBlobs (blobContainer : CloudBlobContainer) =
    let toReservation (b : CloudBlockBlob) =
        let json = b.DownloadText()
        let sr = JsonConvert.DeserializeObject<StoredReservations> json
        sr.Reservations
    let toEnumerator (s : seq<'a>) = s.GetEnumerator()
    let getId (d: DateTime) =
        String.Join(
            "/",
            [
                d.Year.ToString()
                d.Month.ToString()
                d.Day.ToString()
            ])
            |> sprintf "%s.json"

    member this.GetAccessCondition date =
        let id = date |> getId
        let b = blobContainer.GetBlockBlobReference id
        try
            b.FetchAttributes()
            b.Properties.ETag |> AccessCondition.GenerateIfMatchCondition
        with
        | :? StorageException as e when e.RequestInformation.HttpStatusCode = 404 ->
            AccessCondition.GenerateIfNoneMatchCondition "*"

    member this.Write (reservation : Envelope<Reservation>, commandId, condition) =
        let id = reservation.Item.Date |> getId
        let b = blobContainer.GetBlockBlobReference id
        let inStore =
            try
                let jsonInStore = b.DownloadText(accessCondition = condition)
                JsonConvert.DeserializeObject<StoredReservations> jsonInStore
            with
            | :? StorageException as e
                when e.RequestInformation.HttpStatusCode = 404 ->
                    { Reservations = [||]; AcceptedCommandIds = [||] }

        let isReplay =
            inStore.AcceptedCommandIds
            |> Array.exists (fun id -> commandId = id)

        if not isReplay then 
            let updated =
                {
                    Reservations =
                        Array.append [| reservation |] inStore.Reservations
                    AcceptedCommandIds =
                        Array.append [| commandId |] inStore.AcceptedCommandIds
                }

            let json = JsonConvert.SerializeObject updated
            b.Properties.ContentType <- "application/json"
            b.UploadText(json, accessCondition = condition)

    interface IReservations with
        member this.Between min max =
            Dates.InitInfinite min
            |> Seq.takeWhile (fun d -> d <= max)
            |> Seq.map getId
            |> Seq.map blobContainer.GetBlockBlobReference
            |> Seq.filter (fun b -> b.Exists())
            |> Seq.collect toReservation

        member this.GetEnumerator() =
            blobContainer.ListBlobs()
            |> Seq.cast<CloudBlockBlob>
            |> Seq.collect toReservation
            |> toEnumerator

        member this.GetEnumerator() =
            (this :> seq<Envelope<Reservation>>).GetEnumerator() :> System.Collections.IEnumerator

type NotificationsInAzureBlobs(blobContainer : CloudBlobContainer) =
    let toNotification (b : CloudBlockBlob) =
        let json = b.DownloadText()
        JsonConvert.DeserializeObject<Envelope<Notification>> json
    let toEnumerator (s : seq<'a>) = s.GetEnumerator()

    member this.Write notification =
        let id = sprintf "%O/%O.json" notification.Item.About notification.Id
        let b = blobContainer.GetBlockBlobReference id

        let json = JsonConvert.SerializeObject notification
        b.Properties.ContentType <- "application/json"
        b.UploadText json

    interface Notifications.INotifications with
        member this.About id =
            blobContainer.ListBlobs(id.ToString(), true)
            |> Seq.cast<CloudBlockBlob>
            |> Seq.map toNotification

        member this.GetEnumerator() =
            blobContainer.ListBlobs(useFlatBlobListing = true)
            |> Seq.cast<CloudBlockBlob>
            |> Seq.map toNotification
            |> toEnumerator

        member this.GetEnumerator() =
            (this :> seq<Envelope<Notification>>).GetEnumerator() :> System.Collections.IEnumerator

type ErrorsInAzureBlobs(blobContainer : CloudBlobContainer) =
    let getId (d : DateTime) =
        String.Join(
            "/",
            [
                d.Year.ToString()
                d.Month.ToString()
                d.Day.ToString()
                Guid.NewGuid().ToString()
            ])
            |> sprintf "%s.txt"

    member this.Write e =
        let id = getId DateTimeOffset.UtcNow.Date
        let b = blobContainer.GetBlockBlobReference id
        b.Properties.ContentType <- "text/plain; charset=utf-8"
        b.UploadText(e.ToString())

    interface System.Web.Http.Filters.IExceptionFilter with
        member this.AllowMultiple = true
        member this.ExecuteExceptionFilterAsync(actionExecutedContext, cancellationToken) =
            System.Threading.Tasks.Task.Factory.StartNew(
                fun () -> this.Write actionExecutedContext.Exception)

module AzureQ =
    let enqueue (q : Queue.CloudQueue) msg =
        let json = JsonConvert.SerializeObject msg
        Queue.CloudQueueMessage(json) |> q.AddMessage

    let dequeue (q : Queue.CloudQueue) =
        match q.GetMessage() with
        | null -> None
        | msg -> Some(msg)


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

        let storageAccount =
            CloudConfigurationManager.GetSetting "storageConnectionString"
            |> CloudStorageAccount.Parse

        let errorContainer =
            storageAccount
                .CreateCloudBlobClient()
                .GetContainerReference("errors")
        errorContainer.CreateIfNotExists() |> ignore
        let errorHandler = ErrorsInAzureBlobs(errorContainer)

        let rq =
            storageAccount
                .CreateCloudQueueClient()
                .GetQueueReference("reservations")
        rq.CreateIfNotExists() |> ignore

        let reservationsContainer =
            storageAccount
                .CreateCloudBlobClient()
                .GetContainerReference("reservations")
        reservationsContainer.CreateIfNotExists() |> ignore

        let reservations = ReservationsInAzureBlobs(reservationsContainer)        
        
        let nq =
            storageAccount
                .CreateCloudQueueClient()
                .GetQueueReference("notifications")
        nq.CreateIfNotExists() |> ignore
        let notificationsContainer =
            storageAccount
                .CreateCloudBlobClient()
                .GetContainerReference("notifications")
        notificationsContainer.CreateIfNotExists() |> ignore
        let notifications = NotificationsInAzureBlobs(notificationsContainer)

        let reservationSubject = new Subject<Envelope<Reservation> * Guid * AccessCondition>()
        reservationSubject.Subscribe reservations.Write |> ignore

        let notificationSubject = new Subject<Notification>()
        notificationSubject
        |> Observable.map EnvelopWithDefaults
        |> Observable.subscribe (AzureQ.enqueue nq)
        |> ignore

        let handleR (msg : Queue.CloudQueueMessage) =
            try
                let json = msg.AsString
                let cmd =
                    JsonConvert.DeserializeObject<Envelope<MakeReservation>> json
                let condition = reservations.GetAccessCondition cmd.Item.Date
                let newReservations = Handle seatingCapacity reservations cmd
                match newReservations with 
                    | Some(r) ->
                        reservationSubject.OnNext(r, cmd.Id, condition) 
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

                rq.DeleteMessage msg
            with e -> errorHandler.Write e

        System.Reactive.Linq.Observable.Interval(TimeSpan.FromSeconds 10.)
        |> Observable.map (fun _ -> AzureQ.dequeue rq)
        |> Observable.choose id
        |> Observable.subscribe handleR
        |> ignore

        let handleN (msg : Queue.CloudQueueMessage) =
            try
                let json = msg.AsString
                let notification =
                    JsonConvert.DeserializeObject<Envelope<Notification>> json
                notifications.Write notification            
                nq.DeleteMessage msg
            with e -> errorHandler.Write e

        System.Reactive.Linq.Observable.Interval(TimeSpan.FromSeconds 10.)
        |> Observable.map (fun _ -> AzureQ.dequeue nq)
        |> Observable.choose id
        |> Observable.subscribe handleN
        |> ignore
        
        let config = new HttpConfiguration()

        config.Filters.Add errorHandler

        Configure
            reservations
            (Observer.Create (fun msg -> AzureQ.enqueue rq msg))
            notifications
            seatingCapacity
            config
        
        Startup.RegisterWebApi config
        builder.UseWebApi config |> ignore

