# ZxavoxPayments

Payment service for Zxadocs petty cash (plan section 11). It owns provider credentials and calls to
MTN MoMo, Airtel Money and utility billers; Zxadocs owns requests, approvals and accounting.

- `zxavoxpaymentapi` - FastEndpoints host
- `zxavoxpaymentbe` - entities, `PaymentsDbContext` (database `zxavox`, schema `payments_api`), services
- `zxavoxpaymentapi.Tests` - tests on Postgres and RabbitMQ containers (needs Docker)

## Run

1. Copy `env.example` to `zxavoxpaymentapi/.env` and set `PAY_SECRETS_KEY` and `PAY_BOOTSTRAP_API_KEY`.
2. Create the database: `createdb zxavox`, then from `zxavoxpaymentbe`: `dotnet ef database update`.
3. `dotnet run --project zxavoxpaymentapi`, or `docker compose up --build`.

Every route except `/health/*` and `/callbacks/*` needs the `X-Api-Key` header.

In Development the OpenAPI document is at `/openapi/v1.json` and the Scalar explorer at `/scalar`.
