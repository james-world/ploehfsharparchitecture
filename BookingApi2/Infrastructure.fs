module BookingApi2.Infrastructure

open System
open System.Net.Http
open System.Web.Http
open System.Web.Http.Controllers
open System.Web.Http.Dispatcher
open BookingApi2
open Reservations

type Agent<'a> = Microsoft.FSharp.Control.MailboxProcessor<'a>

type CompositionRoot(reservations : System.Collections.Concurrent.ConcurrentBag<Envelope<Reservation>>) =
    let seatingCapacity = 10

    let agent = new Agent<Envelope<MakeReservation>>(fun inbox ->
        let rec loop () =
            async {
                let! cmd = inbox.Receive()
                let rs = reservations |> ToReservations
                let handle = Handle seatingCapacity rs
                let newReservations = handle cmd
                match newReservations with 
                | Some(r) -> reservations.Add r
                | _ -> ()
                return! loop() }
        loop())
    do agent.Start()

    interface IHttpControllerActivator with
        member this.Create(request, controllerDescriptor, controllerType) =
            if controllerType = typeof<HomeController> then
                new HomeController() :> IHttpController
            elif controllerType = typeof<ReservationsController> then
                let c = new ReservationsController()
                let sub = c.Subscribe agent.Post
                request.RegisterForDispose sub
                c :> IHttpController
            else
                invalidArg (sprintf "Unknown controller type requested: %O" controllerType) "controllerType"

let ConfigureServices reservations (config : HttpConfiguration) =
    config.Services.Replace(
        typeof<IHttpControllerActivator>,
        CompositionRoot reservations)

let Configure reservations (config : HttpConfiguration) =
    ConfigureServices reservations config
