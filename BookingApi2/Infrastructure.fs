module BookingApi2.Infrastructure

open System
open System.Net.Http
open System.Reactive
open System.Web.Http
open System.Web.Http.Controllers
open System.Web.Http.Dispatcher
open BookingApi2
open Reservations
open Notifications

type CompositionRoot(reservations : IReservations,
                     reservationRequestObserver : IObserver<Envelope<MakeReservation>>,
                     notifications : INotifications,
                     seatingCapacity) =

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
            elif controllerType = typeof<NotificationsController> then
                let c = new NotificationsController(notifications)
                c :> IHttpController
            elif controllerType = typeof<AvailabilityController> then
                let c = new AvailabilityController(reservations, seatingCapacity)
                c :> IHttpController
            else
                invalidArg (sprintf "Unknown controller type requested: %O" controllerType) "controllerType"

let ConfigureServices
    reservations
    (reservationRequestObserver : IObserver<Envelope<MakeReservation>>)
    notifications
    seatingCapacity
    (config : HttpConfiguration) =
    config.Services.Replace(
        typeof<IHttpControllerActivator>,
        CompositionRoot(reservations,reservationRequestObserver, notifications, seatingCapacity))

let Configure reservations
              (reservationRequestObserver : IObserver<Envelope<MakeReservation>>)
              notifications
              seatingCapacity
              (config : HttpConfiguration) =
    ConfigureServices reservations reservationRequestObserver notifications seatingCapacity config
