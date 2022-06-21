## Purpose
It turned out that retrieving a playername via uuid is a bottleneck. 
This service attempts to remove that.

## Deploying
This project should be deployed within a container. 
### Configuration
See appsettings.json  
This service requires read access to the main db.
