# Azure web route guard deployment

Source branch:

`source/invoice-billing-center-preview-20260714`

Source commit:

`e4973fdf64a509b691568cb88dee871844943653`

Target:

`ca-phd-test-web-westus3`

Public URL:

`https://phd-west-test.onenecklab.com`

Purpose:

Deploy the 056B dashboard-only card route guard while leaving the API and
database unchanged.

The deployment script records the previous immutable image and revision before
changing the Azure Container App and performs rollback if live verification
fails.
