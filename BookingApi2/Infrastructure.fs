module BookingApi2.Infrastructure

open System
open System.Net.Http
open System.Reactive
open System.Web.Http
open System.Web.Http.Controllers
open System.Web.Http.Dispatcher
open BookingApi2
open Reservations

type CompositionRoot(reservations : IReservations,
                     reservationRequestObserver : IObserver<Envelope<MakeReservation>>) =

    interface IHttpControllerActivator with
        member this.Create(request, controllerDescriptor, controllerType) =
            if controllerType = typeof<HomeController> then
                new HomeController() :> IHttpController
            elif controllerType = typeof<ReservationsController> then
                let c = new ReservationsController()
                c
                |> Observable.subscribe reservationRequestObserver.OnNext
                |> request.RegisterForDispose
                c :> IHttpController
            else
                invalidArg (sprintf "Unknown controller type requested: %O" controllerType) "controllerType"

let ConfigureServices reservations (reservationRequestObserver : IObserver<Envelope<MakeReservation>>)  (config : HttpConfiguration) =
    config.Services.Replace(
        typeof<IHttpControllerActivator>,
        CompositionRoot(reservations,reservationRequestObserver))

let Configure reservations (reservationRequestObserver : IObserver<Envelope<MakeReservation>>) (config : HttpConfiguration) =
    ConfigureServices reservations reservationRequestObserver config
