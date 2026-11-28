# Clinic Management System

Clinic Management System is a full-stack demo for clinic directories, referral review, and appointment scheduling. Patients can view their own referrals and appointments. The app uses React and TypeScript, ASP.NET Core, SQL Server, and Auth0.

## Run locally with Docker Compose

Follow the [development setup](docs/setup.md#run-locally-with-docker-compose)
to configure Auth0, initialize SQL Server, and start the stack. Open
`http://localhost:8080` when the web service is healthy.

## Try the demo

[Open the live Clinic Management demo](https://clinic-demo-quan.westus2.cloudapp.azure.com).

Sign in and select **Create my workspace** to try booking and rescheduling
with your own copy of the fictional clinic records. The [demo guide](docs/demo.md)
walks through setup and shows the patient, front-desk, and provider views.

## Documentation

- [Architecture and project layout](docs/architecture.md)
- [API endpoints and pagination](docs/api.md)
- [Local setup and development checks](docs/setup.md)
- [Deployment checks](docs/deployment.md)
- [Database seeds and scale testing](backend/database/ScaleTesting.md)
- [Contributing](CONTRIBUTING.md)
