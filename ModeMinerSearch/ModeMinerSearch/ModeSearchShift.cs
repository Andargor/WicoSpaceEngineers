﻿using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        /*
         * 
         * OLD:
        * 0=init sensors
        * 8 check sensors
        * 1=push down
        * 2=push left ->3
        * 9=push right ->3
        *
        * 3 = check sensors; go to 4 or 5
        * 4 = rotating ->5 or ->7
        * 5. asteroid check on 'bottom' after rotate and nothing in front.
        * 6. rotate after 5 ->7
        * 7. Wait for motion to stop (velocityShip) ->mine
        * 
        * 20. Init search top/left (sensor setting)
        * 30. check sensors
        * 29 move backward until NOT bfrontnasteroid
        * 27. Move forward until bfrontnasteroid
        * 28. delay for motion. reset sensors.

        * * 21. move up until bfront clear
        * 22. move right until roll control clear
        * 23 delay for motion. reset sensors
        * 24 shift left 1/2 ship width - 2m *.35
        * 25 shift down until bfront, bfrontnear. stop if !brollcontrol and do searchverify
        * 26 delay for motion. then start mining
        */
        /*
          * readonly Dictionary < int, string > SearchShiftStates = new Dictionary < int, string > { 
             { 
                 0, "Initializing" 
             }, { 
                 1, "Moving Down" 
             }, { 
                 2, "Moving Left" 
             }, { 
                 3, "Calculate rotate" 
             }, { 
                 4, "Rotating" 
             }, { 
                 5, "Check Bottom" 
             }, { 
                 6, "Rotate" 
             }, { 
                 7, "Delay for motion" 
             }, { 
                 8, "Check Sensors" 
             }, { 
                 9, "Moving Right" 
             }, { 
                 20, "STR:Init search top right" 
             }, { 
                 21, "STR:move up" 
             }, { 
                 22, "STR:move right" 
             }, { 
                 23, "STR:delay for motion" 
             }, { 
                 24, "STR:shift left" 
             }, { 
                 25, "STR:shift down" 
             }, { 
                 26, "STR:delay for motion (2)" 
             }, { 
                 27, "STR:Move in range" 
             }, { 
                 28, "STR:delay for motion (3)" 
             }, { 
                 29, "STR:Move back to clear" 
             }, { 
                 30, "STR:Check Sensors" 
             } 

         }; 
         */

            /* New
             * 0 = master init
             * 10 delay for sensors, then check for asteroid found
             * if found ->20
             * else ->100
             */

        double shiftElapsedMs = 0;

        void doModeSearchShift()
        {
            List<IMySensorBlock> aSensors = null;

            StatusLog("clear", textPanelReport);
            StatusLog(moduleName + ":SearchShift", textPanelReport);
            Echo("Search Shift: current_state=" + current_state.ToString());
            double maxThrust = calculateMaxThrust(thrustForwardList);
            Echo("maxThrust=" + maxThrust.ToString("N0"));

            MyShipMass myMass;
            myMass = ((IMyShipController)gpsCenter).CalculateShipMass();
            double effectiveMass = myMass.PhysicalMass;
            Echo("effectiveMass=" + effectiveMass.ToString("N0"));

            double maxDeltaV = (maxThrust) / effectiveMass;
            Echo("maxDeltaV=" + maxDeltaV.ToString("0.00"));

            Echo("Cargo=" + cargopcent.ToString() + "%");

            Echo("velocity=" + velocityShip.ToString("0.00"));
            Echo("shiftElapsedMs=" + shiftElapsedMs.ToString("0.00"));

            bool bLocalFoundAsteroid = false;

            IMySensorBlock sb;
            IMySensorBlock sb2;
            if (sensorsList.Count < 2)
            {
                StatusLog(OurName + ":" + moduleName + " Search Shift: Not Enough Sensors!", textLongStatus, true);
                setMode(MODE_ATTENTION);
                return;
            }

            sb = sensorsList[0];
            sb2 = sensorsList[1];
            switch (current_state)
            {
                case 0:
                    ResetMotion();
                    sleepAllSensors();
                    sb2.DetectAsteroids = true;
                    sb.DetectAsteroids = true;
                    setSensorShip(sb, 0, 0, 0, 0, 45, 0);
                    current_state = 10;
                    shiftElapsedMs = 0;
                    break;
                case 10:
                    Echo("Check for front");
                    shiftElapsedMs += Runtime.TimeSinceLastRun.TotalMilliseconds;
                    if (shiftElapsedMs < 1) return;// delay for sensors
                    if (velocityShip > 0.2f) return;
                    Echo("Checking");
                    aSensors = activeSensors();
                    bLocalFoundAsteroid = false;
                    Echo(aSensors.Count + ": Sensors active");
                    for (int i = 0; i < aSensors.Count; i++)
                    {
                        IMySensorBlock s = aSensors[i] as IMySensorBlock;
                        Echo(aSensors[i].CustomName + " ACTIVE!");

                        List<MyDetectedEntityInfo> lmyDEI = new List<MyDetectedEntityInfo>();
                        s.DetectedEntities(lmyDEI);
                        if (AsteroidProcessLDEI(lmyDEI))
                        {
                            Echo("Found Asteroid");
                            bLocalFoundAsteroid = true;
                        }
                        else Echo("Did NOT find asteroid");
                    }
                    if (bLocalFoundAsteroid) current_state = 20;
                    else current_state = 100;
                    break;
                case 20:
                    setMode(MODE_FINDORE);
                    break;
                case 100:
                    // asteroid not in sensor range
                    // check 'down' for asteroid.
                    setSensorShip(sb, 0, 0, 0, 0, 45, 0);
                    setSensorShip(sb2, 0, 0, 0, 45, 45, 0);
                    current_state = 110;
                    shiftElapsedMs = 0;
                    break;
                case 110:
                    {
                        // search 'down'
                        shiftElapsedMs += Runtime.TimeSinceLastRun.TotalMilliseconds;
                        if (shiftElapsedMs < 1) return;
                        if (velocityShip > 0.2f) return;

                        bool bDownAsteroid = false;
                        bool bForwardAsteroid = false;
                        bool bLarge = false;
                        bool bSmall =false;
                        SensorActive(sb2, ref bDownAsteroid, ref bLarge, ref bSmall);
                        SensorActive(sb, ref bForwardAsteroid, ref bLarge, ref bSmall);
                        if (bDownAsteroid || bForwardAsteroid) current_state = 120;
                        else current_state = 130;
                        break;
                    }
                case 120:
                    {
                        // we found asteroid 'below'.  Move down

                        // check 'down' for asteroid.
 //                       setSensorShip(sb, 0, 0, 0, 0, 45, 0);
                        // don't need to change sensors
                        current_state = 122;
//                        shiftElapsedMs = 0;
                        break;
                    }
                case 122:
                    {
                        Echo("Move Down");
//                        if (shiftElapsedMs < 1) return;
//                        if (velocityShip > 0.2f) return;
                        // move down until sensor sees asteroid
                        bool bDownAsteroid = false;
                        bool bForwardAsteroid = false;
                        bool bLarge = false;
                        bool bSmall = false;
                        SensorActive(sb2, ref bDownAsteroid, ref bLarge, ref bSmall);
                        SensorActive(sb, ref bForwardAsteroid, ref bLarge, ref bSmall);
                        Echo(bForwardAsteroid + ":" + bDownAsteroid);
                        if (bForwardAsteroid)
                        {
                            Echo("Forward");
                            ResetMotion();
                            // we have it right in front of us.
                            setMode(MODE_FINDORE);
//                            current_state = 120;
                        }
                        else if(bDownAsteroid)
                        {
                            Echo("Down");
                            if (velocityShip < 0.5)
                                powerUpThrusters(thrustDownList, 100f);
                            else if (velocityShip > fTargetMiningMps)
                                powerDownThrusters();
                            else powerUpThrusters(thrustDownList, 1f);
                        }
                        else
                        {
                            Echo("Neither");
                            // we ran out of asteroid.
                            ResetMotion();
                            current_state = 130;
                        }

                        break;
                    }
                case 130:
                    {
                        // No asteroid 'below'.
                        // need to shift (left) (old state 2)
                        Echo("Shift Left");
                        break;
                    }
            }
        }
    }
}