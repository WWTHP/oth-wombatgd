# Diesel sound sources

Raw candidate recordings for the OM617 diesel layer (1980 Mercedes 300GD).
All files are in `raw/`. Every file is a Freesound **HQ preview** (Ogg Vorbis
~192 kbps) fetched from `cdn.freesound.org/previews/...`, not the original
upload. Licenses were read from each sound's own Freesound page on 2026-11-10.

Durations, sample rates and loudness profiles come from `ffprobe`/`ffmpeg`.
Loudness was measured per second (RMS). The "steady" ranges below are where
the level stays within about 1 dB. Nobody has listened to these files yet.

Licenses:

- CC0 1.0: https://creativecommons.org/publicdomain/zero/1.0/
- CC BY 3.0: https://creativecommons.org/licenses/by/3.0/
- CC BY 4.0: https://creativecommons.org/licenses/by/4.0/

CC BY files need credit, for example:
"<title>" by <author>, freesound.org/s/<id>/, licensed CC BY x.0.

## Idle candidates

### 1. `raw/idle_mercedes-200d_interior_start-idle-stop_fs509735.ogg`
- Title: Mercedes Benz 200 D idling. Recorded inside // Mercedes Benz 200 D käynnistys, tyhjäkäynti, sammutus. Sisä-äänitys
- Author: YleArkisto (Yle, the Finnish Broadcasting Company archive)
- Page: https://freesound.org/people/YleArkisto/sounds/509735/
- File: https://cdn.freesound.org/previews/509/509735_4415905-hq.ogg
- License: CC BY 4.0
- Contents: Mercedes 200 D, recorded inside the car in Finland in 1985. The
  engine is an OM615, a naturally aspirated pre-chamber diesel and the
  4-cylinder sibling of the OM617. The clip is crank, idle, then shutdown.
  72.6 s, 48 kHz stereo.
- Measured: crank at about 3.5-5 s. Steady idle from 5 s to 69 s, flat at
  -34 dB, which gives about 60 s of loopable idle. Shutdown at about 69-72 s.
  It was recorded inside the car, so expect a muffled sound and possibly some
  tape hiss from the archive.

### 2. `raw/idle_vw-golf2-1.6d_start-idle-stop_fs425862.ogg`
- Title: Volkswagen Golf II 1.6 Diesel Engine Start Idle Stop Mono.wav
- Author: Soundholder
- Page: https://freesound.org/people/Soundholder/sounds/425862/
- File: https://cdn.freesound.org/previews/425/425862_4421503-hq.ogg
- License: CC BY 3.0. The description also advertises a paid full pack, but
  the Freesound license governs this file.
- Contents: VW 1.6 D, a naturally aspirated indirect-injection 4-cylinder
  diesel. Professional recording of start, idle and stop. 42.9 s,
  96 kHz mono.
- Measured: start in the first second, then very steady idle from about 1 s
  to 39.5 s (-15 dB, ±0.5 dB). Stop at about 40 s. This is the cleanest and
  loudest idle of the set. It is not a Mercedes.

### 3. `raw/idle_mercedes-190d_fs173011.ogg`
- Title: diesel mercedes 190 D.wav
- Author: ninebilly
- Page: https://freesound.org/people/ninebilly/sounds/173011/
- File: https://cdn.freesound.org/previews/173/173011_773642-hq.ogg
- License: CC0 1.0
- Contents: Mercedes 190 D, an OM601 naturally aspirated pre-chamber
  diesel. The page has no description beyond its tags. 45.1 s, 48 kHz stereo.
- Measured: idle at 0-5 s, about 24-33 s and about 37-44 s. Revs at about
  6-12 s and 15-22 s. The best loop is the 9 s idle stretch around 24-33 s.
  The revs might also work as rough load material.

### 4. `raw/idle_mercedes_idle-and-gas_fs399773.ogg`
- Title: Diesel engine idle and gas
- Author: Iridiuss
- Page: https://freesound.org/people/Iridiuss/sounds/399773/
- File: https://cdn.freesound.org/previews/399/399773_1871523-hq.ogg
- License: CC0 1.0
- Contents: "Recorded from a Mercedes." The model is not stated. 14.5 s,
  48 kHz stereo.
- Measured: idle at about 1-5 s (-20 dB, steady), then throttle blips from
  5 s on. The idle stretch is short.

### 5. `raw/idle_mercedes-200d_crawl-idle-shutoff_fs826253.ogg`
- Title: 200dcrawlshutoff.wav
- Author: Mihacappy
- Page: https://freesound.org/people/Mihacappy/sounds/826253/
- File: https://cdn.freesound.org/previews/826/826253_10594370-hq.ogg
- License: CC0 1.0
- Contents: Mercedes 200D crawling past the microphone, then idling and
  shutting off. Recorded on a phone. 30.7 s, 48 kHz stereo.
- Measured: quiet recording (-35 to -38 dB). Idle at about 19-29 s, shutoff
  at about 30 s. Mainly useful as a **shutdown one-shot**.

## Load / rev candidates

### 6. `raw/load_mercedes-om636-180d_rpm-up-down_fs274910.ogg`
- Title: Mercedes_180D_636_accelerate_up_down_rpm.wav
- Author: hantorio
- Page: https://freesound.org/people/hantorio/sounds/274910/
- File: https://cdn.freesound.org/previews/274/274910_1835376-hq.ogg
- License: CC BY 3.0
- Contents: a marine Mercedes OM636 (180D engine), a naturally aspirated
  pre-chamber diesel, recorded on a Zoom H2. RPM goes down and then up.
  28.0 s, 48 kHz stereo.
- Measured: starts high and decays from 0 s to about 18 s, then climbs again
  from about 19 s to 24 s. Highest level at about 0-1 s and 23-25 s. There is
  no long steady plateau. Use a short slice near the top RPM, or treat the
  clip as a sweep. There is no road or tyre noise because it is a marine
  engine.

### 7. `raw/load_tdi_enginebay_drive_fs243783.ogg`
- Title: Diesel-drive.wav
- Author: fastson
- Page: https://freesound.org/people/fastson/sounds/243783/
- File: https://cdn.freesound.org/previews/243/243783_586391-hq.ogg
- License: CC0 1.0
- Contents: Shure SM-58 microphone in the engine bay during a short drive up
  to about 50 km/h. Idle, driving and acceleration. The tags say "TDI", so
  this is a turbo engine and the car is not named. 240.8 s, 48 kHz stereo.
- Measured: idle at 5-24 s (-27 dB, very flat). The longest steady driving
  stretch is about 159-180 s (-21/-22 dB). Peaks at about 91-96 s. Because
  the engine is a TDI, listen for turbo whistle.
- Related CC0 take with the same setup (not downloaded):
  https://freesound.org/people/fastson/sounds/243773/

### 8. `raw/load_vw-passat-tdi_interior_slow-drive_fs89949.ogg`
- Title: car_inside_driving_slow_diesel_engine.wav
- Author: Loffy
- Page: https://freesound.org/people/Loffy/sounds/89949/
- File: https://cdn.freesound.org/previews/89/89949_128303-hq.ogg
- License: CC BY 3.0
- Contents: slow driving in a 2007 VW Passat diesel, a modern turbo engine,
  recorded inside the car. 15.9 s, 44.1 kHz mono.
- Measured: fairly steady at about 0-7 s (-23/-24 dB). This is a fallback
  only, because the modern TDI does not sound like an OM617.

### 9. `raw/load_mercedes-200d_taxi-driveaway_fs510997.ogg`
- Title: Mercedes Benz 200 D. Passenger and drives goes into the taxi, car leaves
- Author: YleArkisto
- Page: https://freesound.org/people/YleArkisto/sounds/510997/
- File: https://cdn.freesound.org/previews/510/510997_4415905-hq.ogg
- License: CC BY 4.0
- Contents: a Mercedes 200 D (OM615) taxi, recorded from outside in 1985. The
  driver opens the door, the passenger gets in, doors close, and the car
  drives off and away. About 73.6 s when fully decoded (the Ogg header
  reports 45.6 s), 48 kHz stereo.
- Measured: doors and handling noise in the first ~17 s, so there may be
  voices. The drive-away under load runs from about 17 s to 35 s, then the
  car recedes. This shows the real engine pulling away, but it is not a
  steady loop.

## Startup one-shot

### 10. `raw/start_mercedes-diesel_outside_fs213729.ogg`
- Title: car start (mercedes diesel) outside
- Author: PITFACTORY
- Page: https://freesound.org/people/PITFACTORY/sounds/213729/
- File: https://cdn.freesound.org/previews/213/213729_4000697-hq.ogg
- License: CC0 1.0
- Contents: an exterior recording of a Mercedes diesel starting, model not
  stated. Recorded on a Zoom H6. 35.7 s, 48 kHz stereo.
- Measured: quiet lead-in from 0 s to 14 s, likely glow plugs or handling.
  Crank and catch at about 15-16 s, running/revving until about 24 s, then
  quieter.

Other start and stop options are #1 (crank at 3.5-5 s, shutdown at 69-72 s,
interior), #2 (start and stop, VW) and #5 (shutoff).

## Recommendation

- **Best idle: #1 YleArkisto Mercedes 200 D (509735).** It is the closest
  engine available (an OM615, the 4-cylinder member of the OM617 family) and
  has about 60 s of very flat idle at 5-69 s, which leaves plenty of room to
  cut a seamless loop. Its crank and shutdown can also be used as one-shots.
  The weakness is the 1985 interior recording: it will be muffled and may
  hiss. If it sounds too dull, **#2 VW Golf II 1.6 D (425862)** is a
  professional, very steady naturally aspirated IDI idle with about 38 s
  usable. A 4-cylinder firing rhythm differs slightly from the OM617's
  5-cylinder rhythm, and pitch-shifting will hide some of that.
- **Best load: #6 hantorio OM636 marine (274910)**, for its engine
  character: a naturally aspirated Mercedes pre-chamber diesel with no turbo
  and no road noise. Cut a short steady slice near the RPM peaks and
  pitch-shift it. If you need a longer steady drive loop, **#7 fastson
  (243783)** has about 20 s of steady driving at 159-180 s. It is a TDI
  though, so check for turbo whistle. None of the load candidates is an ideal
  long steady high-RPM recording of an old Mercedes. A common workaround is
  to pitch the idle loop up and layer a load sample on top.

## Searched but not used

- Wikimedia Commons: `File:OM_67_4.ogg` is a Mercedes OM 67/4, but it is
  CC BY-SA 4.0, which is outside the allowed licenses. The Work With Sounds
  `WWS_FSRTarpan239Dvan*.ogg` files (CC BY 4.0) are van pass-bys and starts
  with an unclear engine, so they were skipped. No other relevant diesel car
  audio was found.
- Freesound, excluded: CC BY-NC sounds 711874 (Mercedes G500 revs, petrol
  V8) and 423607 (Citroën diesel). 705386 is AI-generated, and 640792 is
  mouth-synthesized.
- OpenGameArt: the advanced search returned no usable results from a script,
  and no candidates were found.
