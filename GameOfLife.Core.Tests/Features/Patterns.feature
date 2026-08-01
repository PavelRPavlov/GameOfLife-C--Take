Feature: Multi-generation pattern evolution
  The engine evolves known Conway patterns correctly over many generations,
  including seamless wraparound at the 2^64 torus edge.

  Scenario: A blinker oscillates with period 2
    Given a "B3/S23" world seeded with a horizontal blinker centred at (100, 100)
    When the world advances 1 generation
    Then the live cells are a vertical blinker centred at (100, 100)
    When the world advances 1 generation
    Then the live cells are a horizontal blinker centred at (100, 100)

  Scenario: A glider travels diagonally and wraps across the torus seam
    Given a "B3/S23" world seeded with a glider at the torus origin corner
    When the world advances 4 generations
    Then the glider has translated by (1, 1) with wraparound

  Scenario: A block still life is unchanged after many generations
    Given a "B3/S23" world seeded with a 2x2 block at (50, 50)
    When the world advances 10 generations
    Then the live cells are exactly the 2x2 block at (50, 50)

  Scenario: An all-dead world stays empty
    Given a "B3/S23" world seeded with no live cells
    When the world advances 5 generations
    Then the world has no live cells

  Scenario: A rule containing B0 is rejected
    Then creating a world with rule "B0/S23" is rejected
