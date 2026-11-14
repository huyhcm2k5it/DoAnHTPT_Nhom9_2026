# Controlled seed to implement

Owner: Huy, coordinated with Đức An. V12 section 17 allows initial seed only after all three databases are verified to be a new dataset. Use the SAME configured DatasetId and exactly ten AVAILABLE seats, K=0 and the V12 SeedHash. Do not auto-seed or regenerate DatasetId on restart. Existing nonempty histories require recovery, not reseeding.

No seed script is supplied. Initial activation is separate from seed: verify all three replicas and persist/read back epoch 1 / leader S3 under the trusted operator workflow. Bully candidate nomination alone cannot open serving.
