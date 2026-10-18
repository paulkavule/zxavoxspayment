# zxavoxpaymentapi

The petty cash payment service (ZD-134, plan section 11). It holds provider credentials, talks to MTN
MoMo, Airtel Money and utility billers, and keeps the provider-side record of every payment. Zxadocs
never calls a provider directly: it publishes `payment.requested` and consumes `payment.completed` /
`payment.failed` over RabbitMQ, and calls this API only for beneficiary lookup and read queries.

- `zxavoxpaymentapi/` FastEndpoints host plus background services (consumer, outbox, status poller)
- `zxavoxpaymentbe/` entities, `PaymentsDbContext` (database `zxavoxpayments`, schema `pay`), provider adapters
- `zxavoxpaymentapi.Tests/` tests

Configuration is read from `zxavoxpaymentapi/.env` (see `env.example`).
