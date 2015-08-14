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
                MakeReservation.Date = DateTimeOffset.Parse rendition.Date
                Name = rendition.Name
                Email = rendition.Email
                Quantity = rendition.Quantity }
            |> EnvelopWithDefaults
        subject.OnNext cmd
        this.StatusCode HttpStatusCode.Accepted

    interface IObservable<Envelope<MakeReservation>> with
        member this.Subscribe observer = subject.Subscribe observer

    override this.Dispose disposing =
        if disposing then subject.Dispose()
        base.Dispose disposing