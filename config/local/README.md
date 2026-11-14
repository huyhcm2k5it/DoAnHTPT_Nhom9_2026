# Local process configuration

All six examples share DatasetId `124a4f06-4ac1-4c12-8b27-202600000009`. S1/S2/S3 use 127.0.0.1 with client ports 9001/9002/9003 and peer ports 9101/9102/9103. These endpoints are configuration only: the starter does not listen.

C1/C2/C3 map to owners A/B/C, with isolated `data/clients/c1`, `data/clients/c2`, `data/clients/c3`. C2 and C3 can run on one physical machine. No store is currently created or locked; implement the exclusive pending lock before sending commands (V12 T34).

Server stores are `data/servers/s1`, `s2`, `s3`; configuration validation accepts only the designated relative directory for each process. These folders are ignored by Git. No startup check creates or resets SQL/data.

Use the scripts in the [repository README](../../README.md). `-Check` validates configuration only, without a client window or persistent server wait. Local processes cannot demonstrate three-machine SQL fault tolerance.
