namespace BookingApi2

open System

[<CLIMutable>]
type MakeReservation = {
    Date : DateTimeOffset
    Name : string
    Email : string
    Quantity : int }

[<AutoOpen>]
module Envelope =

    [<CLIMutable>]
    type Envelope<'a> = {
        Id : Guid
        Created : DateTimeOffset
        Item : 'a }

    let Envelop id created item = {
        Id = id
        Created = created
        Item = item }

    let EnvelopWithDefaults item =
        Envelop (Guid.NewGuid()) (DateTimeOffset.UtcNow) item

[<CLIMutable>]
type Reservation = {
    Date : DateTimeOffset
    Name : string
    Email : string
    Quantity : int }