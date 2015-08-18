namespace BookingApi2

open System.Web.Http
open System.Net
open System
open System.Reactive.Subjects

type HomeController() =
    inherit ApiController()

    [<Route("home")>]
    member this.Get() : IHttpActionResult =
        this.Ok() :> _

type ReservationsController() =
    inherit ApiController()

    let subject = new Subject<Envelope<MakeReservation>>()

    [<Route("reservations")>]
    member this.Post (rendition : MakeReservationRendition) =
        let cmd =
            {
                MakeReservation.Date = DateTime.Parse rendition.Date
                Name = rendition.Name
                Email = rendition.Email
                Quantity = rendition.Quantity }
            |> EnvelopWithDefaults
        subject.OnNext cmd

        Results.NegotiatedContentResult(
            HttpStatusCode.Accepted,
            { Links = [| { Rel = "http://bookingapi2/notification"
                           Href = "notifications/" + cmd.Id.ToString "N" } |] },
            this)

    interface IObservable<Envelope<MakeReservation>> with
        member this.Subscribe observer = subject.Subscribe observer

    override this.Dispose disposing =
        if disposing then subject.Dispose()
        base.Dispose disposing

type NotificationsController(notifications : Notifications.INotifications) =
    inherit ApiController()

    [<Route("notifications/{id}")>]
    member this.Get id =
        let toRendition (n : Envelope<Notification>) = {
            About = n.Item.About.ToString()
            Type = n.Item.Type
            Message = n.Item.Message }
        let matches =
            notifications
            |> Notifications.About id
            |> Seq.map toRendition
            |> Seq.toArray

        Results.OkNegotiatedContentResult({ Notifications = matches }, this)

    member this.Notifications = notifications

type AvailabilityController(reservations: Reservations.IReservations,
                            seatingCapacity : int) =
    inherit ApiController()

    let getAvailableSeats map date =
        if map |> Map.containsKey date then
            seatingCapacity - (map |> Map.find date)
        else seatingCapacity

    let getOpeningsIn period =
        let (min, max) = Dates.BoundariesIn period
        let map =
            reservations
            |> Reservations.Between min max
            |> Seq.groupBy (fun r -> r.Item.Date)
            |> Seq.map (fun (d, rs) ->
                (d, rs |> Seq.sumBy (fun r -> r.Item.Quantity)))
            |> Map.ofSeq

        let now = DateTimeOffset.Now
        Dates.In period 
        |> Seq.map (fun d ->
            {
                Date = d.Date.ToString "yyyy.MM.dd"
                Seats = if d < now.Date then 0 else getAvailableSeats map d } )
        |> Seq.toArray

    [<Route("availability/{year}")>]
    member this.Get year =
        let openings = getOpeningsIn <| Year(year)
        Results.OkNegotiatedContentResult({ Openings = openings }, this)

    [<Route("availability/{year}/{month}")>]
    member this.Get(year,month) =
        let openings = getOpeningsIn <| Month(year, month)
        Results.OkNegotiatedContentResult({ Openings = openings }, this)


    [<Route("availability/{year}/{month}/{day}")>]
    member this.Get(year,month, day) =
        let openings = getOpeningsIn <| Day(year, month, day)
        Results.OkNegotiatedContentResult({ Openings = openings }, this)

    member this.SeatingCapacity = seatingCapacity