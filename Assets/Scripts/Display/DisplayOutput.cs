namespace Fortal.Display
{
    // Unity display slots for each installation role (Game view Display 1, 2).
    // Display 1 is the main window, on the LED floor; Display 2 is the wall.
    public static class DisplayOutput
    {
        public const int FloorDisplayIndex = 0;

        public const int WallDisplayIndex = 1;

        // The touch kiosk is retired; anything still aimed at it goes to Display 3, which is never activated.
        public const int RetiredKioskDisplayIndex = 2;
    }
}
